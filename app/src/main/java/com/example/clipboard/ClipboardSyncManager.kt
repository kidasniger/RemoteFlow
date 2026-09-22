package com.example.clipboard

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

class ClipboardSyncManager(private val context: Context) {

    companion object {
        const val MaxTextLength = 1_000_000
    }

    private val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager

    private val _isSyncEnabled = MutableStateFlow(true)
    val isSyncEnabled: StateFlow<Boolean> = _isSyncEnabled.asStateFlow()

    private val _lastSyncedText = MutableStateFlow("")
    val lastSyncedText: StateFlow<String> = _lastSyncedText.asStateFlow()

    private val _syncStatus = MutableStateFlow("En attente")
    val syncStatus: StateFlow<String> = _syncStatus.asStateFlow()

    private var onLocalClipboardChanged: ((String) -> Unit)? = null

    private val clipListener = ClipboardManager.OnPrimaryClipChangedListener {
        if (_isSyncEnabled.value) {
            val clip = clipboard.primaryClip
            if (clip != null && clip.itemCount > 0) {
                val text = clip.getItemAt(0).text?.toString() ?: ""
                if (text.isNotBlank() && text != _lastSyncedText.value) {
                    _lastSyncedText.value = text
                    _syncStatus.value = "Synchronisation vers PC"
                    onLocalClipboardChanged?.invoke(text)
                }
            }
        }
    }

    init {
        clipboard.addPrimaryClipChangedListener(clipListener)
    }

    fun setLocalClipboardSender(sender: (String) -> Unit) {
        onLocalClipboardChanged = sender
    }

    fun setSyncEnabled(enabled: Boolean) {
        _isSyncEnabled.value = enabled
        _syncStatus.value = if (enabled) "Synchronisation active" else "Désactivé"
    }

    fun copyToLocal(text: String, fromRemote: Boolean = false) {
        if (text.length > MaxTextLength)
            return

        // Mark before writing so the local clipboard listener does not echo
        // a Windows-originated update back to the PC.
        _lastSyncedText.value = text
        val clip = ClipData.newPlainText("RemoteFlow", text)
        clipboard.setPrimaryClip(clip)
        _syncStatus.value = if (fromRemote) "Reçu depuis PC" else "Copié localement"
    }

    fun cleanup() {
        clipboard.removePrimaryClipChangedListener(clipListener)
    }
}
