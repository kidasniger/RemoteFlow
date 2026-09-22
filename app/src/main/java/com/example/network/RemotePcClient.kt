package com.example.network

import com.example.domain.model.ConnectionState
import com.example.domain.model.DeviceInfo
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.BufferedReader
import java.io.BufferedWriter
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.net.InetSocketAddress
import java.net.Socket

data class RemoteMouseEvent(
    val type: MouseAction,
    val x: Float = 0f,
    val y: Float = 0f,
    val deltaX: Float = 0f,
    val deltaY: Float = 0f
)

enum class MouseAction {
    MOVE, LEFT_CLICK, RIGHT_CLICK, DOUBLE_CLICK, SCROLL_UP, SCROLL_DOWN
}

data class RemoteKeyEvent(
    val keyLabel: String,
    val isSpecial: Boolean = false,
    val isModifier: Boolean = false
)

data class WhiteboardPoint(val x: Float, val y: Float)

data class WhiteboardPath(
    val points: List<WhiteboardPoint>,
    val colorHex: String,
    val strokeWidth: Float
)

interface RemotePcClient {
    val connectionState: StateFlow<ConnectionState>
    val lastActionLog: StateFlow<String>
    val measuredLatencyMs: StateFlow<Long>
    fun connectWithQr(code: String)
    fun connectManual(ipOrCode: String)
    fun disconnect()
    fun reconnect()
    fun sendMouseEvent(event: RemoteMouseEvent)
    fun sendKeyEvent(event: RemoteKeyEvent)
    fun sendMacroCommand(macroId: String, command: String)
    fun sendWhiteboardStroke(path: WhiteboardPath)
    fun sendClipboard(text: String)
    fun setIncomingClipboardReceiver(receiver: (String) -> Unit)
}

class DefaultRemotePcClient(
    private val scope: CoroutineScope = CoroutineScope(Dispatchers.Main)
) : RemotePcClient {

    private val _connectionState = MutableStateFlow<ConnectionState>(ConnectionState.Disconnected)
    override val connectionState: StateFlow<ConnectionState> = _connectionState.asStateFlow()

    private val _lastActionLog = MutableStateFlow("Non connecté")
    override val lastActionLog: StateFlow<String> = _lastActionLog.asStateFlow()

    private val _measuredLatencyMs = MutableStateFlow(0L)
    override val measuredLatencyMs: StateFlow<Long> = _measuredLatencyMs.asStateFlow()

    private var socket: Socket? = null
    private var writer: BufferedWriter? = null
    private var reader: BufferedReader? = null
    private var readJob: Job? = null
    private var incomingClipboardReceiver: ((String) -> Unit)? = null

    private var lastTargetHost: String = ""
    private var lastTargetPort: Int = 8443

    override fun setIncomingClipboardReceiver(receiver: (String) -> Unit) {
        incomingClipboardReceiver = receiver
    }

    override fun connectWithQr(code: String) {
        // Parse QR content: either "remoteflow://ip:port" or "ip:port" or "ip"
        val cleanCode = code.trim().removePrefix("remoteflow://")
        val parts = cleanCode.split(":")
        val host = parts[0].trim()
        val port = parts.getOrNull(1)?.toIntOrNull() ?: 8443
        performRealConnection(host, port)
    }

    override fun connectManual(ipOrCode: String) {
        val trimmed = ipOrCode.trim()
        val parts = trimmed.split(":")
        val host = parts[0].trim()
        val port = parts.getOrNull(1)?.toIntOrNull() ?: 8443
        performRealConnection(host, port)
    }

    private fun performRealConnection(host: String, port: Int) {
        if (host.isBlank()) {
            _connectionState.value = ConnectionState.Failed("Adresse hôte vide")
            return
        }

        lastTargetHost = host
        lastTargetPort = port
        _connectionState.value = ConnectionState.Connecting("$host:$port")

        scope.launch(Dispatchers.IO) {
            closeExistingSocket()
            val startTime = System.currentTimeMillis()
            try {
                val newSocket = Socket()
                newSocket.tcpNoDelay = true
                newSocket.soTimeout = 5000
                newSocket.connect(InetSocketAddress(host, port), 4000)

                val newWriter = BufferedWriter(OutputStreamWriter(newSocket.getOutputStream()))
                val newReader = BufferedReader(InputStreamReader(newSocket.getInputStream()))

                socket = newSocket
                writer = newWriter
                reader = newReader

                val latency = System.currentTimeMillis() - startTime
                _measuredLatencyMs.value = latency

                withContext(Dispatchers.Main) {
                    _connectionState.value = ConnectionState.Connected(
                        DeviceInfo(
                            name = "PC ($host)",
                            ipAddress = host,
                            port = port,
                            latencyMs = latency,
                            isEncrypted = true
                        )
                    )
                    _lastActionLog.value = "Connecté à $host:$port ($latency ms)"
                }

                // Start incoming listener loop
                readJob = launch(Dispatchers.IO) {
                    try {
                        var line: String?
                        while (newReader.readLine().also { line = it } != null) {
                            line?.let { msg ->
                                withContext(Dispatchers.Main) {
                                    handleIncomingMessage(msg)
                                }
                            }
                        }
                    } catch (_: Exception) {
                    } finally {
                        withContext(Dispatchers.Main) {
                            if (_connectionState.value is ConnectionState.Connected) {
                                _connectionState.value = ConnectionState.Disconnected
                                _lastActionLog.value = "Connexion fermée par le PC"
                            }
                        }
                    }
                }
            } catch (e: Exception) {
                withContext(Dispatchers.Main) {
                    _connectionState.value = ConnectionState.Failed(
                        "Échec de connexion ($host:$port) : ${e.localizedMessage ?: "Serveur injoignable"}"
                    )
                    _lastActionLog.value = "Erreur: ${e.message}"
                }
            }
        }
    }

    override fun disconnect() {
        scope.launch(Dispatchers.IO) {
            closeExistingSocket()
            withContext(Dispatchers.Main) {
                _connectionState.value = ConnectionState.Disconnected
                _lastActionLog.value = "Déconnecté"
            }
        }
    }

    override fun reconnect() {
        if (lastTargetHost.isNotBlank()) {
            performRealConnection(lastTargetHost, lastTargetPort)
        }
    }

    override fun sendMouseEvent(event: RemoteMouseEvent) {
        val json = JSONObject().apply {
            put("action", "mouse")
            put("type", event.type.name)
            put("x", event.x)
            put("y", event.y)
            put("dx", event.deltaX)
            put("dy", event.deltaY)
        }.toString()
        sendRawPayload(json, "Souris: ${event.type}")
    }

    override fun sendKeyEvent(event: RemoteKeyEvent) {
        val json = JSONObject().apply {
            put("action", "keyboard")
            put("key", event.keyLabel)
            put("special", event.isSpecial)
            put("modifier", event.isModifier)
        }.toString()
        sendRawPayload(json, "Touche: ${event.keyLabel}")
    }

    override fun sendMacroCommand(macroId: String, command: String) {
        val json = JSONObject().apply {
            put("action", "macro")
            put("id", macroId)
            put("cmd", command)
        }.toString()
        sendRawPayload(json, "Macro: $command")
    }

    override fun sendWhiteboardStroke(path: WhiteboardPath) {
        val json = JSONObject().apply {
            put("action", "whiteboard")
            put("color", path.colorHex)
            put("width", path.strokeWidth)
            put("pointsCount", path.points.size)
        }.toString()
        sendRawPayload(json, "Tracé (${path.points.size} pts)")
    }

    override fun sendClipboard(text: String) {
        if (text.length > 1_000_000) {
            _lastActionLog.value = "Presse-papiers trop volumineux"
            return
        }

        val json = JSONObject().apply {
            put("action", "clipboard")
            put("text", text)
        }.toString()
        sendRawPayload(json, "Presse-papiers envoyé au PC")
    }

    private fun handleIncomingMessage(rawMessage: String) {
        try {
            val json = JSONObject(rawMessage)
            if (json.optString("event") == "clipboard" &&
                json.optString("source").equals("windows", ignoreCase = true) &&
                json.has("text") &&
                !json.isNull("text")
            ) {
                incomingClipboardReceiver?.invoke(json.getString("text"))
                _lastActionLog.value = "Presse-papiers reçu depuis Windows"
                return
            }
        } catch (_: Exception) {
            // Keep normal logging for malformed or unrelated messages.
        }

        _lastActionLog.value = "Reçu PC: $rawMessage"
    }

    private fun sendRawPayload(payload: String, logLabel: String) {
        scope.launch(Dispatchers.IO) {
            try {
                writer?.let {
                    it.write(payload)
                    it.newLine()
                    it.flush()
                }
                withContext(Dispatchers.Main) {
                    _lastActionLog.value = logLabel
                }
            } catch (e: Exception) {
                withContext(Dispatchers.Main) {
                    _lastActionLog.value = "Erreur émission : ${e.localizedMessage}"
                }
            }
        }
    }

    private fun closeExistingSocket() {
        readJob?.cancel()
        readJob = null
        try {
            writer?.close()
        } catch (_: Exception) {}
        try {
            reader?.close()
        } catch (_: Exception) {}
        try {
            socket?.close()
        } catch (_: Exception) {}
        writer = null
        reader = null
        socket = null
    }
}
