package com.example.clipboard

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

class ClipboardSyncManager(private val context: Context) {

    private val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager

    private val _isSyncEnabled = MutableStateFlow(true)
    val isSyncEnabled: StateFlow<Boolean> = _isSyncEnabled.asStateFlow()

    private val _lastSyncedText = MutableStateFlow("")
    val lastSyncedText: StateFlow<String> = _lastSyncedText.asStateFlow()

    private val _syncStatus = MutableStateFlow("En attente")
    val syncStatus: StateFlow<String> = _syncStatus.asStateFlow()

    private val clipListener = ClipboardManager.OnPrimaryClipChangedListener {
        if (_isSyncEnabled.value) {
            val clip = clipboard.primaryClip
            if (clip != null && clip.itemCount > 0) {
                val text = clip.getItemAt(0).text?.toString() ?: ""
                if (text.isNotBlank() && text != _lastSyncedText.value) {
                    _lastSyncedText.value = text
                    _syncStatus.value = "Synchronisé avec PC"
                }
            }
        }
    }

    init {
        clipboard.addPrimaryClipChangedListener(clipListener)
    }

    fun setSyncEnabled(enabled: Boolean) {
        _isSyncEnabled.value = enabled
        _syncStatus.value = if (enabled) "Synchronisation active" else "Désactivé"
    }

    fun copyToLocal(text: String) {
        val clip = ClipData.newPlainText("RemoteFlow", text)
        clipboard.setPrimaryClip(clip)
        _lastSyncedText.value = text
        _syncStatus.value = "Copié localement"
    }

    fun cleanup() {
        clipboard.removePrimaryClipChangedListener(clipListener)
    }
}
