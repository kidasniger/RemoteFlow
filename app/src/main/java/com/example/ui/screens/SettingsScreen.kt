package com.example.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ArrowForwardIos
import androidx.compose.material.icons.filled.Computer
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.domain.model.ConnectionState
import com.example.network.RemotePcClient
import com.example.settings.SettingsManager
import com.example.ui.theme.BorderLight
import com.example.ui.theme.BorderSubtle
import com.example.ui.theme.DangerRed
import com.example.ui.theme.DarkSlate
import com.example.ui.theme.PrimaryBlue
import com.example.ui.theme.SecondaryCyan
import com.example.ui.theme.StatusConnected
import com.example.ui.theme.SurfaceLight
import com.example.ui.theme.TextMuted
import com.example.ui.theme.TextPrimary
import com.example.ui.theme.TextSecondary

@Composable
fun SettingsScreen(
    settingsManager: SettingsManager,
    remoteClient: RemotePcClient,
    onBackClick: () -> Unit,
    onDisconnect: () -> Unit
) {
    val settings by settingsManager.settings.collectAsState()
    val connectionState by remoteClient.connectionState.collectAsState()

    var showQualityDialog by remember { mutableStateOf(false) }

    val qualityOptions = listOf("Auto • 60fps", "1080p • 60fps", "720p • 30fps", "Faible latence (Jeux)")

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(SurfaceLight)
            .testTag("settings_screen")
    ) {
        // Top Header
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .height(56.dp)
                .background(Color.White)
                .border(1.dp, BorderLight)
                .padding(horizontal = 14.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Box(
                modifier = Modifier
                    .size(36.dp)
                    .clip(CircleShape)
                    .clickable(onClick = onBackClick),
                contentAlignment = Alignment.Center
            ) {
                Icon(
                    imageVector = Icons.AutoMirrored.Filled.ArrowBack,
                    contentDescription = "Retour",
                    tint = TextPrimary,
                    modifier = Modifier.size(20.dp)
                )
            }
            Spacer(modifier = Modifier.width(6.dp))
            Text(
                text = "Paramètres",
                fontSize = 15.sp,
                fontWeight = FontWeight.Bold,
                color = TextPrimary
            )
        }

        Column(
            modifier = Modifier
                .weight(1f)
                .verticalScroll(rememberScrollState())
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            // Connected Device Card
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .shadow(4.dp, RoundedCornerShape(16.dp), spotColor = Color.Black.copy(alpha = 0.05f))
                    .clip(RoundedCornerShape(16.dp))
                    .background(Color.White)
                    .border(1.dp, BorderLight, RoundedCornerShape(16.dp))
                    .padding(14.dp)
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Box(
                            modifier = Modifier
                                .size(44.dp)
                                .clip(RoundedCornerShape(12.dp))
                                .background(DarkSlate),
                            contentAlignment = Alignment.Center
                        ) {
                            Icon(
                                imageVector = Icons.Default.Computer,
                                contentDescription = "PC",
                                tint = SecondaryCyan,
                                modifier = Modifier.size(24.dp)
                            )
                        }

                        Spacer(modifier = Modifier.width(12.dp))

                        Column {
                            val deviceName = if (connectionState is ConnectionState.Connected) {
                                (connectionState as ConnectionState.Connected).device.name
                            } else "PC Connecté"
                            Text(
                                text = deviceName,
                                fontSize = 13.sp,
                                fontWeight = FontWeight.Bold,
                                color = TextPrimary
                            )
                            Spacer(modifier = Modifier.height(2.dp))
                            Text(
                                text = "Wi-Fi 5GHz • Latence 4ms",
                                fontSize = 11.sp,
                                color = StatusConnected,
                                fontWeight = FontWeight.Medium
                            )
                        }
                    }

                    Text(
                        text = "Déconnecter",
                        fontSize = 11.sp,
                        color = DangerRed,
                        fontWeight = FontWeight.Bold,
                        modifier = Modifier
                            .clickable {
                                remoteClient.disconnect()
                                onDisconnect()
                            }
                            .padding(6.dp)
                            .testTag("settings_disconnect_button")
                    )
                }
            }

            // Group: General Settings
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .shadow(3.dp, RoundedCornerShape(16.dp), spotColor = Color.Black.copy(alpha = 0.04f))
                    .clip(RoundedCornerShape(16.dp))
                    .background(Color.White)
                    .border(1.dp, BorderLight, RoundedCornerShape(16.dp))
            ) {
                // Notifications switch
                SettingSwitchRow(
                    title = "Notifications",
                    subtitle = "Alertes de transfert et notifications PC",
                    checked = settings.notifications,
                    onCheckedChange = { settingsManager.updateNotifications(it) }
                )

                Box(modifier = Modifier.fillMaxWidth().height(1.dp).background(BorderLight))

                // Universal Clipboard
                SettingSwitchRow(
                    title = "Presse-papiers universel",
                    subtitle = "Synchronisation automatique texte et liens",
                    checked = settings.universalClipboard,
                    onCheckedChange = { settingsManager.updateUniversalClipboard(it) }
                )

                Box(modifier = Modifier.fillMaxWidth().height(1.dp).background(BorderLight))

                // Dark Theme
                SettingSwitchRow(
                    title = "Mode Sombre",
                    subtitle = "Apparence assombrie de l'interface",
                    checked = settings.darkTheme,
                    onCheckedChange = { settingsManager.updateDarkTheme(it) }
                )
            }

            // Group: Advanced & Security
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .shadow(3.dp, RoundedCornerShape(16.dp), spotColor = Color.Black.copy(alpha = 0.04f))
                    .clip(RoundedCornerShape(16.dp))
                    .background(Color.White)
                    .border(1.dp, BorderLight, RoundedCornerShape(16.dp))
            ) {
                // Streaming Quality
                SettingActionRow(
                    title = "Qualité Streaming",
                    value = settings.streamingQuality,
                    onClick = { showQualityDialog = true }
                )

                Box(modifier = Modifier.fillMaxWidth().height(1.dp).background(BorderLight))

                // End-to-End Security
                SettingActionRow(
                    title = "Sécurité Bout en Bout",
                    value = if (settings.e2eSecurity) "Activé (AES-256)" else "Désactivé",
                    onClick = { settingsManager.updateSecurity(!settings.e2eSecurity) }
                )

                Box(modifier = Modifier.fillMaxWidth().height(1.dp).background(BorderLight))

                // Version Info
                SettingActionRow(
                    title = "Version RemoteFlow",
                    value = "v1.0.0 (Native Android)",
                    onClick = {}
                )
            }
        }
    }

    if (showQualityDialog) {
        AlertDialog(
            onDismissRequest = { showQualityDialog = false },
            title = { Text("Qualité Streaming", fontWeight = FontWeight.Bold) },
            text = {
                Column {
                    qualityOptions.forEach { option ->
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable {
                                    settingsManager.updateStreamingQuality(option)
                                    showQualityDialog = false
                                }
                                .padding(vertical = 8.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            RadioButton(
                                selected = (option == settings.streamingQuality),
                                onClick = {
                                    settingsManager.updateStreamingQuality(option)
                                    showQualityDialog = false
                                }
                            )
                            Spacer(modifier = Modifier.width(8.dp))
                            Text(text = option, fontSize = 13.sp)
                        }
                    }
                }
            },
            confirmButton = {
                TextButton(onClick = { showQualityDialog = false }) {
                    Text("Fermer", color = PrimaryBlue)
                }
            }
        )
    }
}

@Composable
private fun SettingSwitchRow(
    title: String,
    subtitle: String,
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 16.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(text = title, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = TextPrimary)
            Spacer(modifier = Modifier.height(2.dp))
            Text(text = subtitle, fontSize = 10.5.sp, color = TextSecondary)
        }
        Switch(
            checked = checked,
            onCheckedChange = onCheckedChange,
            colors = SwitchDefaults.colors(
                checkedThumbColor = Color.White,
                checkedTrackColor = PrimaryBlue,
                uncheckedThumbColor = Color.White,
                uncheckedTrackColor = BorderSubtle
            )
        )
    }
}

@Composable
private fun SettingActionRow(
    title: String,
    value: String,
    onClick: () -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Text(text = title, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = TextPrimary)
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(text = value, fontSize = 11.5.sp, color = TextMuted)
            Spacer(modifier = Modifier.width(6.dp))
            Icon(
                imageVector = Icons.AutoMirrored.Filled.ArrowForwardIos,
                contentDescription = null,
                tint = TextMuted,
                modifier = Modifier.size(12.dp)
            )
        }
    }
}
