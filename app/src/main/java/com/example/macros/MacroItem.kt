package com.example.macros

enum class MacroIconType {
    POWER, LOCK, PLAY, MUTE, SCRIPT, CAPTURE, BROWSER, BOOST, CUSTOM
}

data class MacroItem(
    val id: String,
    val label: String,
    val iconType: MacroIconType,
    val colorHex: Long,
    val command: String,
    val isCustom: Boolean = false
)

object DefaultMacros {
    val list = listOf(
        MacroItem("1", "Power Off", MacroIconType.POWER, 0xFFEF4444, "shutdown /s /t 0"),
        MacroItem("2", "Verrouiller", MacroIconType.LOCK, 0xFF005CFF, "user32.dll,LockWorkStation"),
        MacroItem("3", "Play Media", MacroIconType.PLAY, 0xFF2EE5C8, "media_play_pause"),
        MacroItem("4", "Mute", MacroIconType.MUTE, 0xFF64748B, "media_mute"),
        MacroItem("5", "Run Script", MacroIconType.SCRIPT, 0xFF0F172A, "powershell -File ./task.ps1"),
        MacroItem("6", "Capture", MacroIconType.CAPTURE, 0xFF8B5CF6, "screenshot_to_clipboard"),
        MacroItem("7", "Chrome", MacroIconType.BROWSER, 0xFFF59E0B, "start chrome"),
        MacroItem("8", "Boost", MacroIconType.BOOST, 0xFF06B6D4, "optimize_ram_cache"),
        MacroItem("9", "Custom", MacroIconType.CUSTOM, 0xFF005CFF, "echo 'RemoteFlow Custom'")
    )
}
