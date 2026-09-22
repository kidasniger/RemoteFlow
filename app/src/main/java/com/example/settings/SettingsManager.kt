package com.example.settings

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

data class UserSettings(
    val notifications: Boolean = true,
    val universalClipboard: Boolean = true,
    val darkTheme: Boolean = false,
    val streamingQuality: String = "Auto • 60fps",
    val e2eSecurity: Boolean = true,
    val touchSensitivity: Float = 0.75f
)

class SettingsManager(context: Context) {

    private val prefs = context.getSharedPreferences("remoteflow_prefs", Context.MODE_PRIVATE)

    private val _settings = MutableStateFlow(
        UserSettings(
            notifications = prefs.getBoolean("notifications", true),
            universalClipboard = prefs.getBoolean("universal_clipboard", true),
            darkTheme = prefs.getBoolean("dark_theme", false),
            streamingQuality = prefs.getString("streaming_quality", "Auto • 60fps") ?: "Auto • 60fps",
            e2eSecurity = prefs.getBoolean("e2e_security", true),
            touchSensitivity = prefs.getFloat("touch_sensitivity", 0.75f)
        )
    )
    val settings: StateFlow<UserSettings> = _settings.asStateFlow()

    fun updateNotifications(enabled: Boolean) {
        prefs.edit().putBoolean("notifications", enabled).apply()
        _settings.value = _settings.value.copy(notifications = enabled)
    }

    fun updateUniversalClipboard(enabled: Boolean) {
        prefs.edit().putBoolean("universal_clipboard", enabled).apply()
        _settings.value = _settings.value.copy(universalClipboard = enabled)
    }

    fun updateDarkTheme(enabled: Boolean) {
        prefs.edit().putBoolean("dark_theme", enabled).apply()
        _settings.value = _settings.value.copy(darkTheme = enabled)
    }

    fun updateStreamingQuality(quality: String) {
        prefs.edit().putString("streaming_quality", quality).apply()
        _settings.value = _settings.value.copy(streamingQuality = quality)
    }

    fun updateSecurity(enabled: Boolean) {
        prefs.edit().putBoolean("e2e_security", enabled).apply()
        _settings.value = _settings.value.copy(e2eSecurity = enabled)
    }

    fun updateSensitivity(value: Float) {
        prefs.edit().putFloat("touch_sensitivity", value).apply()
        _settings.value = _settings.value.copy(touchSensitivity = value)
    }
}
