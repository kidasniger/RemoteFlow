package com.example.files

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import android.util.Base64
import com.example.network.RemoteFileEvent
import com.example.network.RemotePcClient
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.io.RandomAccessFile
import java.util.UUID

class FileManager(
    private val context: Context,
    private val remoteClient: RemotePcClient? = null,
    private val scope: CoroutineScope = CoroutineScope(Dispatchers.Main)
) {
    private data class DownloadState(
        val transferId: String,
        val sharedFile: SharedFile,
        val outputFile: File,
        val file: RandomAccessFile,
        val totalBytes: Long,
        val completion: CompletableDeferred<Boolean>
    )

    private val _phoneFiles = MutableStateFlow<List<SharedFile>>(emptyList())
    val phoneFiles: StateFlow<List<SharedFile>> = _phoneFiles.asStateFlow()

    private val _pcFiles = MutableStateFlow<List<SharedFile>>(emptyList())
    val pcFiles: StateFlow<List<SharedFile>> = _pcFiles.asStateFlow()

    private val _selectedFiles = MutableStateFlow<Set<String>>(emptySet())
    val selectedFiles: StateFlow<Set<String>> = _selectedFiles.asStateFlow()

    private val _transferProgress = MutableStateFlow(0f)
    val transferProgress: StateFlow<Float> = _transferProgress.asStateFlow()

    private val _isTransferring = MutableStateFlow(false)
    val isTransferring: StateFlow<Boolean> = _isTransferring.asStateFlow()

    private var transferJob: Job? = null
    private val incomingEvents = Channel<RemoteFileEvent>(Channel.UNLIMITED)
    private val activeDownloads = mutableMapOf<String, DownloadState>()

    init {
        loadLocalAppFiles()

        remoteClient?.setIncomingFileEventReceiver { event ->
            incomingEvents.trySend(event)
        }

        scope.launch(Dispatchers.IO) {
            for (event in incomingEvents) {
                processRemoteFileEvent(event)
            }
        }
    }

    private fun loadLocalAppFiles() {
        val filesDir = context.getExternalFilesDir(null) ?: context.filesDir
        val files = filesDir.listFiles()?.filter { it.isFile } ?: emptyList()
        _phoneFiles.value = files.map { file ->
            SharedFile(
                id = UUID.nameUUIDFromBytes(file.absolutePath.toByteArray()).toString(),
                name = file.name,
                sizeBytes = file.length(),
                formattedSize = formatFileSize(file.length()),
                isLocal = true,
                uri = Uri.fromFile(file)
            )
        }
    }

    fun refreshPcFiles() {
        remoteClient?.requestPcFiles()
    }

    fun addFilesFromUris(uris: List<Uri>) {
        val newFiles = uris.mapNotNull { uri ->
            try {
                var name = "RemoteFlow-file"
                var size = 0L

                context.contentResolver.query(
                    uri,
                    null,
                    null,
                    null,
                    null
                )?.use { cursor ->
                    val nameIndex = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                    val sizeIndex = cursor.getColumnIndex(OpenableColumns.SIZE)
                    if (cursor.moveToFirst()) {
                        if (nameIndex >= 0) name = cursor.getString(nameIndex)
                        if (sizeIndex >= 0 && !cursor.isNull(sizeIndex)) {
                            size = cursor.getLong(sizeIndex)
                        }
                    }
                }

                SharedFile(
                    id = UUID.randomUUID().toString(),
                    name = name,
                    sizeBytes = size,
                    formattedSize = formatFileSize(size),
                    isLocal = true,
                    uri = uri
                )
            } catch (_: Exception) {
                null
            }
        }

        _phoneFiles.value = newFiles + _phoneFiles.value
    }

    fun toggleFileSelection(fileId: String) {
        _selectedFiles.value =
            if (_selectedFiles.value.contains(fileId)) {
                _selectedFiles.value - fileId
            } else {
                _selectedFiles.value + fileId
            }
    }

    fun sendSelectedFilesToPc(onResult: (Boolean, String) -> Unit) {
        val selected = _phoneFiles.value.filter {
            _selectedFiles.value.contains(it.id)
        }

        if (selected.isEmpty()) {
            onResult(false, "Aucun fichier sélectionné")
            return
        }

        transferJob?.cancel()
        transferJob = scope.launch(Dispatchers.IO) {
            try {
                _isTransferring.emit(true)
                _transferProgress.emit(0f)

                val total = selected.sumOf { maxOf(it.sizeBytes, 0L) }.coerceAtLeast(1L)
                var sent = 0L

                for (file in selected) {
                    val client = remoteClient ?: throw IllegalStateException("PC non connecté")
                    val uri = file.uri ?: continue
                    val size = file.sizeBytes.coerceAtLeast(0L)
                    val id = "android-up-" + UUID.randomUUID()

                    client.sendFileCommand(
                        type = "UPLOAD_START",
                        transferId = id,
                        fileName = sanitizeFileName(file.name),
                        size = size,
                        offset = 0
                    )

                    context.contentResolver.openInputStream(uri)?.use { input ->
                        val buffer = ByteArray(CHUNK_SIZE)
                        var offset = 0L

                        while (true) {
                            val read = input.read(buffer)
                            if (read <= 0) break

                            client.sendFileCommand(
                                type = "UPLOAD_CHUNK",
                                transferId = id,
                                offset = offset,
                                dataBase64 = Base64.encodeToString(
                                    buffer,
                                    0,
                                    read,
                                    Base64.NO_WRAP
                                )
                            )

                            offset += read
                            sent += read
                            _transferProgress.emit(
                                (sent.toFloat() / total).coerceIn(0f, 1f)
                            )
                        }
                    } ?: throw IllegalStateException("Lecture impossible : " + file.name)

                    client.sendFileCommand(
                        type = "UPLOAD_END",
                        transferId = id,
                        offset = size,
                        totalBytes = size
                    )
                }

                withContext(Dispatchers.Main) {
                    _isTransferring.value = false
                    _transferProgress.value = 1f
                    _selectedFiles.value = emptySet()
                    refreshPcFiles()
                    onResult(true, "Transfert vers le PC terminé")
                }
            } catch (e: Exception) {
                withContext(Dispatchers.Main) {
                    _isTransferring.value = false
                    _transferProgress.value = 0f
                    onResult(
                        false,
                        "Erreur transfert : " + (e.localizedMessage ?: "inconnue")
                    )
                }
            }
        }
    }

    fun receiveSelectedFilesFromPc(onResult: (Boolean, String) -> Unit) {
        val selected = _pcFiles.value.filter {
            _selectedFiles.value.contains(it.id)
        }

        if (selected.isEmpty()) {
            onResult(false, "Aucun fichier PC sélectionné")
            return
        }

        transferJob?.cancel()
        transferJob = scope.launch(Dispatchers.IO) {
            try {
                _isTransferring.emit(true)
                _transferProgress.emit(0f)

                for (remote in selected) {
                    val client = remoteClient
                        ?: throw IllegalStateException("PC non connecté")
                    val id = "android-dl-" + UUID.randomUUID()
                    val local = createSafeDownloadFile(remote.name)
                    local.parentFile?.mkdirs()
                    val raf = RandomAccessFile(local, "rw")
                    raf.setLength(0)

                    val completion = CompletableDeferred<Boolean>()
                    val shared = SharedFile(
                        id = UUID.randomUUID().toString(),
                        name = local.name,
                        sizeBytes = remote.sizeBytes,
                        formattedSize = formatFileSize(remote.sizeBytes),
                        isLocal = true,
                        uri = Uri.fromFile(local),
                        status = TransferStatus.TRANSFERRING
                    )

                    synchronized(activeDownloads) {
                        activeDownloads[id] = DownloadState(
                            transferId = id,
                            sharedFile = shared,
                            outputFile = local,
                            file = raf,
                            totalBytes = remote.sizeBytes,
                            completion = completion
                        )
                    }

                    client.sendFileCommand(
                        type = "DOWNLOAD_START",
                        transferId = id,
                        fileName = remote.name,
                        path = remote.name,
                        offset = 0
                    )

                    if (!completion.await()) {
                        throw IllegalStateException("Téléchargement interrompu : " + remote.name)
                    }
                }

                withContext(Dispatchers.Main) {
                    _isTransferring.value = false
                    _transferProgress.value = 1f
                    _selectedFiles.value = emptySet()
                    loadLocalAppFiles()
                    onResult(true, "Téléchargement depuis le PC terminé")
                }
            } catch (e: Exception) {
                withContext(Dispatchers.Main) {
                    _isTransferring.value = false
                    _transferProgress.value = 0f
                    onResult(
                        false,
                        "Erreur téléchargement : " + (e.localizedMessage ?: "inconnue")
                    )
                }
            }
        }
    }

    fun cancelTransfer() {
        transferJob?.cancel()
        scope.launch(Dispatchers.IO) {
            val downloads = synchronized(activeDownloads) {
                activeDownloads.values.toList()
            }

            for (state in downloads) {
                try {
                    remoteClient?.sendFileCommand(
                        type = "DOWNLOAD_CANCEL",
                        transferId = state.transferId,
                        offset = 0
                    )
                } catch (_: Exception) {
                }
                try { state.file.close() } catch (_: Exception) {}
                state.completion.complete(false)
            }

            synchronized(activeDownloads) {
                activeDownloads.clear()
            }

            withContext(Dispatchers.Main) {
                _isTransferring.value = false
                _transferProgress.value = 0f
            }
        }
    }

    private suspend fun processRemoteFileEvent(event: RemoteFileEvent) {
        when (event) {
            is RemoteFileEvent.List -> {
                _pcFiles.emit(
                    event.files.map { remote ->
                        SharedFile(
                            id = "pc:" + remote.relativePath,
                            name = remote.name,
                            sizeBytes = remote.size,
                            formattedSize = formatFileSize(remote.size),
                            isLocal = false
                        )
                    }
                )
            }

            is RemoteFileEvent.Chunk -> {
                val state = synchronized(activeDownloads) {
                    activeDownloads[event.transferId]
                } ?: return

                if (event.offset < 0 ||
                    event.offset != state.outputFile.length()
                ) {
                    finishDownload(state, false, "Offset de chunk invalide")
                    return
                }

                try {
                    val bytes = Base64.decode(
                        event.dataBase64,
                        Base64.DEFAULT
                    )

                    if (bytes.isNotEmpty()) {
                        state.file.seek(event.offset)
                        state.file.write(bytes)

                        val received = event.offset + bytes.size
                        _transferProgress.emit(
                            if (state.totalBytes > 0) {
                                (received.toFloat() / state.totalBytes).coerceIn(0f, 1f)
                            } else {
                                1f
                            }
                        )

                        if (event.isFinal && received >= state.totalBytes) {
                            finishDownload(state, true, null)
                        }
                    }
                } catch (e: Exception) {
                    finishDownload(state, false, e.localizedMessage)
                }
            }

            is RemoteFileEvent.State -> {
                val state = synchronized(activeDownloads) {
                    activeDownloads[event.transferId]
                } ?: return

                when (event.state.lowercase()) {
                    "completed" -> finishDownload(state, true, null)
                    "cancelled", "error" -> finishDownload(
                        state,
                        false,
                        event.error ?: event.state
                    )
                    else -> {
                        if (event.totalBytes > 0) {
                            _transferProgress.emit(
                                (event.offset.toFloat() / event.totalBytes)
                                    .coerceIn(0f, 1f)
                            )
                        }
                    }
                }
            }
        }
    }

    private suspend fun finishDownload(
        state: DownloadState,
        success: Boolean,
        error: String?
    ) {
        synchronized(activeDownloads) {
            activeDownloads.remove(state.transferId)
        }

        try { state.file.close() } catch (_: Exception) {}

        if (success) {
            _phoneFiles.emit(
                _phoneFiles.value + state.sharedFile.copy(
                    status = TransferStatus.SUCCESS,
                    progress = 1f
                )
            )
        } else {
            try { state.outputFile.delete() } catch (_: Exception) {}
        }

        if (!state.completion.isCompleted) {
            state.completion.complete(success)
        }
    }

    private fun sanitizeFileName(name: String): String =
        name.replace(Regex("[\\/:*?\"<>|]"), "_")
            .take(240)
            .ifBlank { "RemoteFlow-file" }

    private fun createSafeDownloadFile(name: String): File {
        val root = File(
            context.getExternalFilesDir(null) ?: context.filesDir,
            "RemoteFlow-Downloads"
        )
        root.mkdirs()

        val safeName = sanitizeFileName(name)
        val file = File(root, safeName)
        val rootPath = root.canonicalFile.path
        val filePath = file.canonicalFile.path

        if (!filePath.startsWith(rootPath + File.separator)) {
            throw SecurityException("Chemin de téléchargement interdit")
        }

        return file
    }

    private fun formatFileSize(bytes: Long): String =
        when {
            bytes >= 1024 * 1024 * 1024 ->
                String.format("%.1f GB", bytes / (1024.0 * 1024 * 1024))
            bytes >= 1024 * 1024 ->
                String.format("%.1f MB", bytes / (1024.0 * 1024))
            bytes >= 1024 ->
                String.format("%.1f KB", bytes / 1024.0)
            bytes > 0 -> bytes.toString() + " B"
            else -> "0 B"
        }

    companion object {
        private const val CHUNK_SIZE = 32 * 1024
    }
}
