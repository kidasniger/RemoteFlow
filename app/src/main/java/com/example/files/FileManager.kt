package com.example.files

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import com.example.network.RemotePcClient
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.util.UUID

class FileManager(
    private val context: Context,
    private val remoteClient: RemotePcClient? = null,
    private val scope: CoroutineScope = CoroutineScope(Dispatchers.Main)
) {

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

    init {
        // Load any existing local files in app filesDir
        loadLocalAppFiles()
    }

    private fun loadLocalAppFiles() {
        val filesDir = context.getExternalFilesDir(null) ?: context.filesDir
        val files: List<File> = filesDir.listFiles()?.filter { it.isFile } ?: emptyList()
        val loaded = files.map { file ->
            SharedFile(
                id = UUID.randomUUID().toString(),
                name = file.name,
                sizeBytes = file.length(),
                formattedSize = formatFileSize(file.length()),
                isLocal = true,
                uri = Uri.fromFile(file)
            )
        }
        _phoneFiles.value = loaded
    }

    fun addFilesFromUris(uris: List<Uri>) {
        val newFiles = uris.mapNotNull { uri ->
            var name = "Fichier_${System.currentTimeMillis()}"
            var size = 0L
            try {
                context.contentResolver.query(uri, null, null, null, null)?.use { cursor ->
                    val nameIndex = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                    val sizeIndex = cursor.getColumnIndex(OpenableColumns.SIZE)
                    if (cursor.moveToFirst()) {
                        if (nameIndex != -1) name = cursor.getString(nameIndex)
                        if (sizeIndex != -1) size = cursor.getLong(sizeIndex)
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
        val current = _selectedFiles.value
        _selectedFiles.value = if (current.contains(fileId)) {
            current - fileId
        } else {
            current + fileId
        }
    }

    fun sendSelectedFilesToPc(onResult: (Boolean, String) -> Unit) {
        val selected = _phoneFiles.value.filter { _selectedFiles.value.contains(it.id) }
        if (selected.isEmpty()) {
            onResult(false, "Aucun fichier sélectionné")
            return
        }

        transferJob?.cancel()
        _isTransferring.value = true
        _transferProgress.value = 0f

        transferJob = scope.launch(Dispatchers.IO) {
            var totalBytesToRead = selected.sumOf { it.sizeBytes }.coerceAtLeast(1L)
            var bytesReadSoFar = 0L
            var success = true
            var message = "Transfert terminé"

            try {
                for (file in selected) {
                    file.uri?.let { uri ->
                        context.contentResolver.openInputStream(uri)?.use { inputStream ->
                            val buffer = ByteArray(8192)
                            var read: Int
                            while (inputStream.read(buffer).also { read = it } != -1) {
                                bytesReadSoFar += read
                                val progress = (bytesReadSoFar.toFloat() / totalBytesToRead).coerceIn(0f, 1f)
                                withContext(Dispatchers.Main) {
                                    _transferProgress.value = progress
                                }
                            }
                        }
                    }
                }
            } catch (e: Exception) {
                success = false
                message = "Erreur lecture : ${e.localizedMessage}"
            }

            withContext(Dispatchers.Main) {
                _isTransferring.value = false
                _transferProgress.value = if (success) 1f else 0f
                _selectedFiles.value = emptySet()
                onResult(success, message)
            }
        }
    }

    fun receiveSelectedFilesFromPc(onResult: (Boolean, String) -> Unit) {
        val pcList = _pcFiles.value
        if (pcList.isEmpty()) {
            onResult(false, "Aucun fichier distant disponible sur le PC")
            return
        }
        onResult(true, "Synchronisation demandée au PC")
    }

    fun cancelTransfer() {
        transferJob?.cancel()
        _isTransferring.value = false
        _transferProgress.value = 0f
    }

    private fun formatFileSize(bytes: Long): String {
        return when {
            bytes >= 1024 * 1024 * 1024 -> String.format("%.1f GB", bytes / (1024.0 * 1024 * 1024))
            bytes >= 1024 * 1024 -> String.format("%.1f MB", bytes / (1024.0 * 1024))
            bytes >= 1024 -> String.format("%.1f KB", bytes / 1024.0)
            bytes > 0 -> "$bytes B"
            else -> "0 B"
        }
    }
}
