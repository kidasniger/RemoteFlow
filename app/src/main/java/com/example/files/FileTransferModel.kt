package com.example.files

import android.net.Uri

enum class TransferStatus {
    IDLE, TRANSFERRING, SUCCESS, ERROR, CANCELLED
}

data class SharedFile(
    val id: String,
    val name: String,
    val sizeBytes: Long,
    val formattedSize: String,
    val isLocal: Boolean, // true if on phone, false if on PC
    val uri: Uri? = null,
    val status: TransferStatus = TransferStatus.IDLE,
    val progress: Float = 0f
)
