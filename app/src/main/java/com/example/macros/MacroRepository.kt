package com.example.macros

import com.example.network.RemotePcClient
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.util.UUID

class MacroRepository(private val remoteClient: RemotePcClient) {

    private val _macros = MutableStateFlow<List<MacroItem>>(DefaultMacros.list)
    val macros: StateFlow<List<MacroItem>> = _macros.asStateFlow()

    private val _lastTriggeredMacro = MutableStateFlow<String?>(null)
    val lastTriggeredMacro: StateFlow<String?> = _lastTriggeredMacro.asStateFlow()

    fun triggerMacro(macro: MacroItem) {
        _lastTriggeredMacro.value = macro.label
        remoteClient.sendMacroCommand(macro.id, macro.command)
    }

    fun addCustomMacro(label: String, command: String, colorHex: Long) {
        val newMacro = MacroItem(
            id = UUID.randomUUID().toString(),
            label = label,
            iconType = MacroIconType.CUSTOM,
            colorHex = colorHex,
            command = command,
            isCustom = true
        )
        _macros.value = _macros.value + newMacro
    }

    fun clearFeedback() {
        _lastTriggeredMacro.value = null
    }
}
