package com.example.domain.model

sealed interface ConnectionState {
    object Disconnected : ConnectionState
    data class Connecting(val target: String) : ConnectionState
    data class PairingRequired(
        val device: DeviceInfo,
        val pinLength: Int,
        val tlsFingerprint: String
    ) : ConnectionState
    data class Connected(val device: DeviceInfo) : ConnectionState
    data class Failed(val reason: String) : ConnectionState
}

data class DeviceInfo(
    val name: String,
    val ipAddress: String,
    val port: Int = 8443,
    val latencyMs: Long = 0,
    val isEncrypted: Boolean = false
)
