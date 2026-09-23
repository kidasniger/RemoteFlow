package com.example.network

import android.content.Context
import android.content.SharedPreferences
import android.net.Uri
import android.os.Build
import android.util.Base64
import com.example.domain.model.ConnectionState
import com.example.domain.model.DeviceInfo
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedReader
import java.io.BufferedWriter
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.charset.StandardCharsets
import java.security.KeyFactory
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.Signature
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.security.spec.X509EncodedKeySpec
import java.util.Locale
import java.util.UUID
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLParameters
import javax.net.ssl.SSLSocket
import javax.net.ssl.X509TrustManager

data class RemoteMouseEvent(
    val type: MouseAction,
    val x: Float = 0f,
    val y: Float = 0f,
    val deltaX: Float = 0f,
    val deltaY: Float = 0f
)

enum class MouseAction {
    MOVE, MOVE_RELATIVE, LEFT_CLICK, RIGHT_CLICK, DOUBLE_CLICK, SCROLL_UP, SCROLL_DOWN
}

data class RemoteKeyEvent(
    val keyLabel: String,
    val isSpecial: Boolean = false,
    val isModifier: Boolean = false,
    val chord: String? = null
)

data class WhiteboardPoint(val x: Float, val y: Float)

data class WhiteboardPath(
    val points: List<WhiteboardPoint>,
    val colorHex: String,
    val strokeWidth: Float
)

data class RemoteMediaFrame(
    val sequence: Long,
    val timestamp: Long,
    val width: Int,
    val height: Int,
    val fps: Int? = null,
    val quality: Int? = null,
    val format: String,
    val dataBase64: String
)

data class RemotePcFileInfo(
    val name: String,
    val relativePath: String,
    val size: Long,
    val lastModifiedUtc: String
)

sealed interface RemoteFileEvent {
    data class List(
        val files: kotlin.collections.List<RemotePcFileInfo>,
        val root: String,
        val truncated: Boolean
    ) : RemoteFileEvent

    data class Chunk(
        val transferId: String,
        val offset: Long,
        val totalBytes: Long,
        val isFinal: Boolean,
        val dataBase64: String
    ) : RemoteFileEvent

    data class State(
        val transferId: String,
        val state: String,
        val offset: Long,
        val totalBytes: Long,
        val fileName: String?,
        val error: String?
    ) : RemoteFileEvent
}

interface RemotePcClient {
    val connectionState: StateFlow<ConnectionState>
    val lastActionLog: StateFlow<String>
    val measuredLatencyMs: StateFlow<Long>
    val screenFrame: StateFlow<RemoteMediaFrame?>
    val webcamFrame: StateFlow<RemoteMediaFrame?>

    fun connectWithQr(code: String)
    fun connectManual(ipOrCode: String)
    fun disconnect()
    fun reconnect()
    fun pair(pin: String)

    fun sendMouseEvent(event: RemoteMouseEvent)
    fun sendKeyEvent(event: RemoteKeyEvent)
    fun sendMacroCommand(macroId: String, command: String)
    fun sendWhiteboardStroke(path: WhiteboardPath)
    fun sendClipboard(text: String)

    fun startScreenStream(
        fps: Int = 8,
        maxWidth: Int = 1280,
        quality: Int = 60,
        screenIndex: Int = -1
    )

    fun stopScreenStream()

    fun startRemoteWebcam(
        cameraIndex: Int = 0,
        width: Int = 1280,
        height: Int = 720,
        fps: Int = 15,
        quality: Int = 70
    )

    fun stopRemoteWebcam()

    fun requestPcFiles()

    suspend fun sendFileCommand(
        type: String,
        transferId: String? = null,
        fileName: String? = null,
        path: String? = null,
        size: Long? = null,
        offset: Long? = null,
        dataBase64: String? = null
    )

    fun setIncomingClipboardReceiver(receiver: (String) -> Unit)
    fun setIncomingFileEventReceiver(receiver: (RemoteFileEvent) -> Unit)
}

class DefaultRemotePcClient(
    context: Context,
    private val scope: CoroutineScope = CoroutineScope(Dispatchers.Main)
) : RemotePcClient {

    private data class ConnectionTarget(
        val host: String,
        val port: Int,
        val expectedTlsFingerprint: String?,
        val expectedDeviceId: String?
    )

    private val prefs = context.applicationContext.getSharedPreferences(
        "remoteflow_security",
        Context.MODE_PRIVATE
    )

    private val clientDeviceId: String =
        prefs.getString("device_id", null)
            ?: UUID.randomUUID().toString().replace("-", "").also {
                prefs.edit().putString("device_id", it).apply()
            }

    private val clientName: String by lazy {
        listOf(Build.MANUFACTURER, Build.MODEL)
            .filter { it.isNotBlank() }
            .joinToString(" ")
            .ifBlank { "Android" }
    }

    private val writeMutex = Mutex()

    private val _connectionState =
        MutableStateFlow<ConnectionState>(ConnectionState.Disconnected)
    override val connectionState: StateFlow<ConnectionState> =
        _connectionState.asStateFlow()

    private val _lastActionLog = MutableStateFlow("Non connecté")
    override val lastActionLog: StateFlow<String> = _lastActionLog.asStateFlow()

    private val _measuredLatencyMs = MutableStateFlow(0L)
    override val measuredLatencyMs: StateFlow<Long> = _measuredLatencyMs.asStateFlow()

    private val _screenFrame = MutableStateFlow<RemoteMediaFrame?>(null)
    override val screenFrame: StateFlow<RemoteMediaFrame?> = _screenFrame.asStateFlow()

    private val _webcamFrame = MutableStateFlow<RemoteMediaFrame?>(null)
    override val webcamFrame: StateFlow<RemoteMediaFrame?> = _webcamFrame.asStateFlow()

    private var socket: SSLSocket? = null
    private var writer: BufferedWriter? = null
    private var reader: BufferedReader? = null
    private var readJob: Job? = null
    private var heartbeatJob: Job? = null
    private var lastTarget: ConnectionTarget? = null

    private var incomingClipboardReceiver: ((String) -> Unit)? = null
    private var incomingFileEventReceiver: ((RemoteFileEvent) -> Unit)? = null

    override fun setIncomingClipboardReceiver(receiver: (String) -> Unit) {
        incomingClipboardReceiver = receiver
    }

    override fun setIncomingFileEventReceiver(receiver: (RemoteFileEvent) -> Unit) {
        incomingFileEventReceiver = receiver
    }

    override fun connectWithQr(code: String) {
        try {
            val target = parseTarget(code)
            lastTarget = target
            performRealConnection(target)
        } catch (e: Exception) {
            _connectionState.value = ConnectionState.Failed(
                "Code RemoteFlow invalide : " + (e.localizedMessage ?: "adresse incorrecte")
            )
        }
    }

    override fun connectManual(ipOrCode: String) {
        connectWithQr(ipOrCode)
    }

    private fun performRealConnection(target: ConnectionTarget) {
        if (target.host.isBlank()) {
            _connectionState.value = ConnectionState.Failed("Adresse hôte vide")
            return
        }

        _connectionState.value =
            ConnectionState.Connecting(target.host + ":" + target.port)

        scope.launch(Dispatchers.IO) {
            closeExistingSocket()
            val startedAt = System.currentTimeMillis()

            try {
                val rawSocket = Socket()
                rawSocket.tcpNoDelay = true
                rawSocket.keepAlive = true
                rawSocket.connect(
                    InetSocketAddress(target.host, target.port),
                    CONNECT_TIMEOUT_MS
                )

                val trustManager = PinningTrustManager(
                    prefs = prefs,
                    pinKey = fingerprintPreferenceKey(target.host, target.port),
                    expectedFingerprint = target.expectedTlsFingerprint
                )

                val sslContext = SSLContext.getInstance("TLS").apply {
                    init(null, arrayOf<X509TrustManager>(trustManager), SecureRandom())
                }

                val sslSocket = sslContext.socketFactory.createSocket(
                    rawSocket,
                    target.host,
                    target.port,
                    true
                ) as SSLSocket

                val supported = sslSocket.supportedProtocols.toSet()
                val enabled = arrayOf("TLSv1.3", "TLSv1.2")
                    .filter { supported.contains(it) }
                if (enabled.isNotEmpty()) {
                    sslSocket.enabledProtocols = enabled.toTypedArray()
                }

                sslSocket.sslParameters = SSLParameters().apply {
                    endpointIdentificationAlgorithm = null
                }
                sslSocket.startHandshake()

                val actualFingerprint = trustManager.observedFingerprint
                    ?: fingerprint(
                        sslSocket.session.peerCertificates.firstOrNull() as? X509Certificate
                            ?: throw CertificateException("Certificat TLS absent")
                    )

                if (!target.expectedTlsFingerprint.isNullOrBlank() &&
                    !sameFingerprint(target.expectedTlsFingerprint, actualFingerprint)
                ) {
                    throw CertificateException("Empreinte TLS différente de celle du QR Code")
                }

                prefs.edit()
                    .putString(
                        fingerprintPreferenceKey(target.host, target.port),
                        actualFingerprint
                    )
                    .apply()

                sslSocket.soTimeout = 0
                socket = sslSocket
                writer = BufferedWriter(
                    OutputStreamWriter(
                        sslSocket.outputStream,
                        StandardCharsets.UTF_8
                    )
                )
                reader = BufferedReader(
                    InputStreamReader(
                        sslSocket.inputStream,
                        StandardCharsets.UTF_8
                    )
                )

                val latency = System.currentTimeMillis() - startedAt
                _measuredLatencyMs.value = latency
                _lastActionLog.value =
                    "TLS établi • empreinte " + actualFingerprint.take(16) + "…"

                readJob = launch(Dispatchers.IO) {
                    listenLoop(
                        sslSocket = sslSocket,
                        localReader = reader!!,
                        target = target,
                        actualTlsFingerprint = actualFingerprint,
                        latencyMs = latency
                    )
                }

                sendRawPayloadSuspend(
                    JSONObject()
                        .put("action", "hello")
                        .put("clientDeviceId", clientDeviceId)
                        .put("clientName", clientName)
                        .toString()
                )
                startHeartbeat()
            } catch (e: Exception) {
                closeExistingSocket()
                withContext(Dispatchers.Main) {
                    _connectionState.value = ConnectionState.Failed(
                        e.localizedMessage ?: "Connexion TLS impossible"
                    )
                    _lastActionLog.value =
                        "Erreur : " + (e.localizedMessage ?: "connexion refusée")
                }
            }
        }
    }

    private suspend fun listenLoop(
        sslSocket: SSLSocket,
        localReader: BufferedReader,
        target: ConnectionTarget,
        actualTlsFingerprint: String,
        latencyMs: Long
    ) {
        try {
            while (!sslSocket.isClosed) {
                val line = localReader.readLine() ?: break
                if (line.isBlank()) continue
                handleIncomingMessage(
                    rawMessage = line,
                    target = target,
                    actualTlsFingerprint = actualTlsFingerprint,
                    latencyMs = latencyMs
                )
            }
        } catch (e: Exception) {
            withContext(Dispatchers.Main) {
                if (_connectionState.value !is ConnectionState.Failed) {
                    _connectionState.value = ConnectionState.Disconnected
                    _lastActionLog.value =
                        "Connexion fermée : " + (e.localizedMessage ?: "déconnecté")
                }
            }
        } finally {
            closeExistingSocket()
        }
    }

    private fun handleIncomingMessage(
        rawMessage: String,
        target: ConnectionTarget,
        actualTlsFingerprint: String,
        latencyMs: Long
    ) {
        try {
            val json = JSONObject(rawMessage)
            val event = json.optString("event")

            when (event) {
                "hello" -> handleHello(
                    json,
                    target,
                    actualTlsFingerprint,
                    latencyMs
                )

                "pairing" -> {
                    if (json.optBoolean("ok", false)) {
                        _connectionState.value = ConnectionState.Connected(
                            connectedDevice(target, latencyMs)
                        )
                        _lastActionLog.value =
                            "Appairage accepté • contrôle distant autorisé"
                    } else {
                        _connectionState.value = ConnectionState.Failed(
                            json.optString("error", "PIN incorrect")
                        )
                        _lastActionLog.value = "Appairage refusé"
                    }
                }

                "clipboard" -> {
                    if (json.optString("source").equals("windows", ignoreCase = true) &&
                        json.has("text") &&
                        !json.isNull("text")
                    ) {
                        incomingClipboardReceiver?.invoke(json.getString("text"))
                        _lastActionLog.value =
                            "Presse-papiers reçu depuis Windows"
                    }
                }

                "screen_frame" -> {
                    _screenFrame.value = RemoteMediaFrame(
                        sequence = json.optLong("sequence"),
                        timestamp = json.optLong("timestamp"),
                        width = json.optInt("width"),
                        height = json.optInt("height"),
                        quality = json.optInt("quality").takeIf { json.has("quality") },
                        format = json.optString("format", "jpeg"),
                        dataBase64 = json.getString("data")
                    )
                }

                "webcam_frame" -> {
                    _webcamFrame.value = RemoteMediaFrame(
                        sequence = json.optLong("sequence"),
                        timestamp = json.optLong("timestamp"),
                        width = json.optInt("width"),
                        height = json.optInt("height"),
                        fps = json.optInt("fps").takeIf { json.has("fps") },
                        quality = json.optInt("quality").takeIf { json.has("quality") },
                        format = json.optString("format", "jpeg"),
                        dataBase64 = json.getString("data")
                    )
                }

                "file_list" -> handleFileList(json)

                "file_chunk" -> incomingFileEventReceiver?.invoke(
                    RemoteFileEvent.Chunk(
                        transferId = json.optString("transferId"),
                        offset = json.optLong("offset"),
                        totalBytes = json.optLong("totalBytes"),
                        isFinal = json.optBoolean("final", false),
                        dataBase64 = json.optString("data")
                    )
                )

                "file_transfer" -> incomingFileEventReceiver?.invoke(
                    RemoteFileEvent.State(
                        transferId = json.optString("transferId"),
                        state = json.optString("state"),
                        offset = json.optLong("offset"),
                        totalBytes = json.optLong("totalBytes"),
                        fileName = json.optString("fileName").takeIf { it.isNotBlank() },
                        error = json.optString("error").takeIf { it.isNotBlank() }
                    )
                )

                "ack" -> {
                    val action = json.optString("action", "action")
                    _lastActionLog.value = if (json.optBoolean("ok", false)) {
                        "ACK reçu • " + action
                    } else {
                        "Erreur PC • " + action + " : " +
                            json.optString("error", "inconnue")
                    }
                }

                "screen_stream", "webcam_stream" -> {
                    _lastActionLog.value =
                        event + " • " + json.optString("state")
                }

                else -> {
                    _lastActionLog.value =
                        "Reçu PC : " + rawMessage.take(180)
                }
            }
        } catch (e: Exception) {
            _lastActionLog.value =
                "Message PC invalide : " + (e.localizedMessage ?: "JSON")
        }
    }

    private fun handleFileList(json: JSONObject) {
        val filesJson = json.optJSONArray("files") ?: JSONArray()
        val files = mutableListOf<RemotePcFileInfo>()

        for (index in 0 until filesJson.length()) {
            val item = filesJson.optJSONObject(index) ?: continue
            files += RemotePcFileInfo(
                name = item.optString("name"),
                relativePath = item.optString(
                    "relativePath",
                    item.optString("name")
                ),
                size = item.optLong("size"),
                lastModifiedUtc = item.optString("lastModifiedUtc")
            )
        }

        incomingFileEventReceiver?.invoke(
            RemoteFileEvent.List(
                files = files,
                root = json.optString("root"),
                truncated = json.optBoolean("truncated", false)
            )
        )
        _lastActionLog.value = files.size.toString() + " fichier(s) PC reçus"
    }

    private fun handleHello(
        json: JSONObject,
        target: ConnectionTarget,
        actualTlsFingerprint: String,
        latencyMs: Long
    ) {
        val deviceId = json.optString("deviceId")
        if (deviceId.isBlank()) {
            throwSecurityFailure("Identité Windows absente")
            return
        }

        if (!target.expectedDeviceId.isNullOrBlank() &&
            !target.expectedDeviceId.equals(deviceId, ignoreCase = true)
        ) {
            throwSecurityFailure("Identité Windows différente de celle du QR Code")
            return
        }

        val tlsFingerprint = json.optString("tlsFingerprint")
        if (tlsFingerprint.isNotBlank() &&
            !sameFingerprint(tlsFingerprint, actualTlsFingerprint)
        ) {
            throwSecurityFailure("Empreinte TLS du serveur incohérente")
            return
        }

        if (!verifyHelloSignature(json)) {
            throwSecurityFailure("Signature cryptographique du serveur invalide")
            return
        }

        val device = connectedDevice(target, latencyMs)
        val pairingRequired = json.optBoolean("pairingRequired", false)
        val pinLength = json.optInt("pinLength", 6).coerceIn(1, 32)

        if (pairingRequired) {
            _connectionState.value =
                ConnectionState.PairingRequired(device, pinLength)
            _lastActionLog.value =
                "Connexion TLS vérifiée • PIN RemoteFlow requis"
        } else {
            _connectionState.value = ConnectionState.Connected(device)
            _lastActionLog.value =
                "Connexion TLS vérifiée • contrôle distant prêt"
        }
    }

    private fun verifyHelloSignature(json: JSONObject): Boolean {
        return try {
            val version = json.optInt("version", 1)
            val deviceId = json.optString("deviceId")
            val port = json.optInt(
                "port",
                lastTarget?.port ?: DEFAULT_PORT
            )
            val nonce = json.optString("nonce")
            val publicKeyBase64 = json.optString("publicKey")
            val signatureBase64 = json.optString("signature")

            if (deviceId.isBlank() ||
                nonce.isBlank() ||
                publicKeyBase64.isBlank() ||
                signatureBase64.isBlank()
            ) {
                return false
            }

            val publicKey = KeyFactory.getInstance("EC").generatePublic(
                X509EncodedKeySpec(
                    Base64.decode(publicKeyBase64, Base64.DEFAULT)
                )
            )

            val verifier = Signature.getInstance("SHA256withECDSA")
            verifier.initVerify(publicKey)
            val canonical =
                "RemoteFlow|" + version + "|" + deviceId + "|" + port + "|" + nonce
            verifier.update(canonical.toByteArray(StandardCharsets.UTF_8))
            verifier.verify(
                Base64.decode(signatureBase64, Base64.DEFAULT)
            )
        } catch (_: Exception) {
            false
        }
    }

    private fun connectedDevice(
        target: ConnectionTarget,
        latencyMs: Long
    ) = DeviceInfo(
        name = "PC (" + target.host + ")",
        ipAddress = target.host,
        port = target.port,
        latencyMs = latencyMs,
        isEncrypted = true
    )

    override fun pair(pin: String) {
        val cleanPin = pin.trim()
        if (!cleanPin.all { it.isDigit() } || cleanPin.length !in 1..32) {
            _lastActionLog.value = "PIN invalide"
            return
        }

        sendRawPayload(
            JSONObject()
                .put("action", "pair")
                .put("pin", cleanPin)
                .put("clientDeviceId", clientDeviceId)
                .put("clientName", clientName)
                .toString(),
            "PIN d'appairage envoyé"
        )
    }

    override fun disconnect() {
        scope.launch(Dispatchers.IO) {
            closeExistingSocket()
            withContext(Dispatchers.Main) {
                _connectionState.value = ConnectionState.Disconnected
                _lastActionLog.value = "Déconnecté"
                _screenFrame.value = null
                _webcamFrame.value = null
            }
        }
    }

    override fun reconnect() {
        lastTarget?.let { performRealConnection(it) }
    }

    override fun sendMouseEvent(event: RemoteMouseEvent) {
        val protocolType = when {
            event.type == MouseAction.MOVE_RELATIVE -> "MOVE_RELATIVE"
            event.type == MouseAction.MOVE &&
                (event.deltaX != 0f || event.deltaY != 0f) -> "MOVE_RELATIVE"
            else -> event.type.name
        }

        val json = JSONObject()
            .put("action", "mouse")
            .put("type", protocolType)
            .apply {
                if (protocolType == "MOVE") {
                    put("x", event.x.coerceIn(0f, 1f))
                    put("y", event.y.coerceIn(0f, 1f))
                } else if (protocolType == "MOVE_RELATIVE") {
                    put("dx", event.deltaX)
                    put("dy", event.deltaY)
                }
            }
            .toString()

        sendRawPayload(json, "Souris : " + protocolType)
    }

    override fun sendKeyEvent(event: RemoteKeyEvent) {
        val json = JSONObject()
            .put("action", "keyboard")
            .put("key", event.keyLabel)
            .put("special", event.isSpecial)
            .put("modifier", event.isModifier)
            .apply {
                event.chord?.takeIf { it.isNotBlank() }?.let {
                    put("chord", it)
                }
            }
            .toString()

        sendRawPayload(
            json,
            "Clavier : " + (event.chord ?: event.keyLabel)
        )
    }

    override fun sendMacroCommand(macroId: String, command: String) {
        sendRawPayload(
            JSONObject()
                .put("action", "macro")
                .put("id", macroId)
                .put("cmd", command)
                .toString(),
            "Macro : " + command
        )
    }

    override fun sendWhiteboardStroke(path: WhiteboardPath) {
        val points = JSONArray()
        path.points.take(MAX_WHITEBOARD_POINTS).forEach { point ->
            points.put(
                JSONObject()
                    .put("x", point.x.coerceIn(0f, 1f))
                    .put("y", point.y.coerceIn(0f, 1f))
            )
        }

        sendRawPayload(
            JSONObject()
                .put("action", "whiteboard")
                .put("color", path.colorHex)
                .put("width", path.strokeWidth)
                .put("pointsCount", points.length())
                .put("points", points)
                .toString(),
            "Tracé : " + points.length() + " pts"
        )
    }

    override fun sendClipboard(text: String) {
        if (text.length > MAX_CLIPBOARD_TEXT) {
            _lastActionLog.value = "Presse-papiers trop volumineux"
            return
        }

        sendRawPayload(
            JSONObject()
                .put("action", "clipboard")
                .put("text", text)
                .toString(),
            "Presse-papiers envoyé au PC"
        )
    }

    override fun startScreenStream(
        fps: Int,
        maxWidth: Int,
        quality: Int,
        screenIndex: Int
    ) {
        sendRawPayload(
            JSONObject()
                .put("action", "screen")
                .put("type", "START")
                .put("fps", fps.coerceIn(1, 15))
                .put("maxWidth", maxWidth.coerceIn(480, 2560))
                .put("quality", quality.coerceIn(30, 90))
                .put("screenIndex", screenIndex)
                .toString(),
            "Streaming écran demandé"
        )
    }

    override fun stopScreenStream() {
        sendRawPayload(
            JSONObject()
                .put("action", "screen")
                .put("type", "STOP")
                .toString(),
            "Arrêt streaming écran demandé"
        )
        _screenFrame.value = null
    }

    override fun startRemoteWebcam(
        cameraIndex: Int,
        width: Int,
        height: Int,
        fps: Int,
        quality: Int
    ) {
        sendRawPayload(
            JSONObject()
                .put("action", "webcam")
                .put("type", "START")
                .put("cameraIndex", cameraIndex.coerceAtLeast(0))
                .put("frameWidth", width.coerceIn(320, 1920))
                .put("frameHeight", height.coerceIn(240, 1080))
                .put("fps", fps.coerceIn(1, 30))
                .put("quality", quality.coerceIn(40, 95))
                .toString(),
            "Webcam PC demandée"
        )
    }

    override fun stopRemoteWebcam() {
        sendRawPayload(
            JSONObject()
                .put("action", "webcam")
                .put("type", "STOP")
                .toString(),
            "Arrêt webcam PC demandé"
        )
        _webcamFrame.value = null
    }

    override fun requestPcFiles() {
        sendRawPayload(
            JSONObject()
                .put("action", "files")
                .put("type", "LIST")
                .toString(),
            "Liste des fichiers PC demandée"
        )
    }

    override suspend fun sendFileCommand(
        type: String,
        transferId: String?,
        fileName: String?,
        path: String?,
        size: Long?,
        offset: Long?,
        dataBase64: String?
    ) {
        val json = JSONObject()
            .put("action", "files")
            .put("type", type)
            .apply {
                transferId?.let { put("transferId", it) }
                fileName?.let { put("fileName", it) }
                path?.let { put("path", it) }
                size?.let { put("size", it) }
                offset?.let { put("offset", it) }
                dataBase64?.let { put("data", it) }
            }
            .toString()

        sendRawPayloadSuspend(json)
    }

    private fun sendRawPayload(payload: String, logLabel: String) {
        scope.launch(Dispatchers.IO) {
            try {
                sendRawPayloadSuspend(payload)
                withContext(Dispatchers.Main) {
                    _lastActionLog.value = logLabel
                }
            } catch (e: Exception) {
                withContext(Dispatchers.Main) {
                    _lastActionLog.value =
                        "Erreur émission : " + (e.localizedMessage ?: "non connecté")
                }
            }
        }
    }

    private suspend fun sendRawPayloadSuspend(payload: String) {
        writeMutex.withLock {
            val activeWriter = writer
                ?: throw IllegalStateException("Non connecté au PC")
            activeWriter.write(payload)
            activeWriter.newLine()
            activeWriter.flush()
        }
    }

    private fun startHeartbeat() {
        heartbeatJob?.cancel()
        heartbeatJob = scope.launch(Dispatchers.IO) {
            while (isActive) {
                delay(20_000L)
                try {
                    if (socket != null) {
                        sendRawPayloadSuspend(
                            JSONObject()
                                .put("action", "hello")
                                .put("clientDeviceId", clientDeviceId)
                                .put("clientName", clientName)
                                .toString()
                        )
                    }
                } catch (_: Exception) {
                    break
                }
            }
        }
    }

    private fun throwSecurityFailure(message: String) {
        _connectionState.value = ConnectionState.Failed(message)
        _lastActionLog.value = "Sécurité : " + message
        scope.launch(Dispatchers.IO) {
            closeExistingSocket()
        }
    }

    private fun closeExistingSocket() {
        heartbeatJob?.cancel()
        heartbeatJob = null
        readJob?.cancel()
        readJob = null
        try { writer?.close() } catch (_: Exception) {}
        try { reader?.close() } catch (_: Exception) {}
        try { socket?.close() } catch (_: Exception) {}
        writer = null
        reader = null
        socket = null
    }

    private fun parseTarget(code: String): ConnectionTarget {
        val trimmed = code.trim()
        if (trimmed.isBlank()) {
            throw IllegalArgumentException("Code vide")
        }

        val uri = if (trimmed.startsWith("remoteflow://", ignoreCase = true)) {
            Uri.parse(trimmed)
        } else {
            Uri.parse("remoteflow://" + trimmed)
        }

        val host = uri.host?.trim().orEmpty()
        val port = if (uri.port in 1..65535) uri.port else DEFAULT_PORT

        if (host.isBlank()) {
            throw IllegalArgumentException("Hôte introuvable")
        }

        return ConnectionTarget(
            host = host,
            port = port,
            expectedTlsFingerprint = uri.getQueryParameter("tlsfp")
                ?.trim()
                ?.takeIf { it.isNotBlank() },
            expectedDeviceId = uri.getQueryParameter("device")
                ?.trim()
                ?.takeIf { it.isNotBlank() }
        )
    }

    private fun fingerprintPreferenceKey(host: String, port: Int): String =
        "tlsfp_" + host + "_" + port

    private class PinningTrustManager(
        private val prefs: SharedPreferences,
        private val pinKey: String,
        private val expectedFingerprint: String?
    ) : X509TrustManager {

        var observedFingerprint: String? = null
            private set

        override fun checkServerTrusted(
            chain: Array<out X509Certificate>,
            authType: String
        ) {
            val certificate = chain.firstOrNull()
                ?: throw CertificateException("Chaîne de certificats TLS vide")

            val now = System.currentTimeMillis()
            if (now < certificate.notBefore.time ||
                now > certificate.notAfter.time
            ) {
                throw CertificateException("Certificat TLS expiré ou pas encore valide")
            }

            val observed = fingerprint(certificate)
            observedFingerprint = observed

            val pinned = normalizeFingerprint(expectedFingerprint)
                ?: normalizeFingerprint(prefs.getString(pinKey, null))

            if (pinned != null &&
                pinned != normalizeFingerprint(observed)
            ) {
                throw CertificateException("Empreinte TLS refusée")
            }
        }

        override fun checkClientTrusted(
            chain: Array<out X509Certificate>,
            authType: String
        ) = Unit

        override fun getAcceptedIssuers(): Array<X509Certificate> =
            emptyArray()
    }

    companion object {
        private const val DEFAULT_PORT = 8443
        private const val CONNECT_TIMEOUT_MS = 5000
        private const val MAX_CLIPBOARD_TEXT = 1_000_000
        private const val MAX_WHITEBOARD_POINTS = 5000

        private fun fingerprint(certificate: X509Certificate): String =
            MessageDigest.getInstance("SHA-256")
                .digest(certificate.encoded)
                .joinToString("") {
                    String.format(Locale.US, "%02X", it)
                }

        private fun normalizeFingerprint(value: String?): String? =
            value
                ?.replace(":", "")
                ?.replace(" ", "")
                ?.trim()
                ?.uppercase(Locale.US)
                ?.takeIf { it.isNotBlank() }

        private fun sameFingerprint(a: String?, b: String?): Boolean =
            normalizeFingerprint(a) == normalizeFingerprint(b)
    }
}
