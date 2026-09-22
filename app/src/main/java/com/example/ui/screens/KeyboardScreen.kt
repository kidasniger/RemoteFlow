package com.example.ui.screens

import androidx.compose.foundation.background
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
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.network.RemoteKeyEvent
import com.example.network.RemotePcClient
import com.example.ui.theme.DarkNavy
import com.example.ui.theme.DarkSlate
import com.example.ui.theme.PrimaryBlue
import com.example.ui.theme.SecondaryCyan

@Composable
fun KeyboardScreen(
    remoteClient: RemotePcClient,
    onBackClick: () -> Unit
) {
    var textBuffer by remember { mutableStateOf("") }

    val row1 = listOf("Esc", "Tab", "Ctrl", "Alt", "Win", "Del")
    val row2 = listOf("Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P")
    val row3 = listOf("A", "S", "D", "F", "G", "H", "J", "K", "L")
    val row4 = listOf("Shift", "Z", "X", "C", "V", "B", "N", "M", "⌫")

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(DarkSlate)
            .testTag("keyboard_screen")
    ) {
        // Header
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .height(56.dp)
                .background(DarkNavy)
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
                    tint = Color.White,
                    modifier = Modifier.size(20.dp)
                )
            }
            Spacer(modifier = Modifier.size(8.dp))
            Text(
                text = "Clavier Virtuel PC",
                color = Color.White,
                fontSize = 15.sp,
                fontWeight = FontWeight.Bold
            )
        }

        // Live Typed Preview Box
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp)
                .height(80.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(DarkNavy)
                .padding(12.dp),
            contentAlignment = Alignment.TopStart
        ) {
            Column {
                Text(
                    text = "Aperçu de la frappe en temps réel :",
                    color = Color.White.copy(alpha = 0.5f),
                    fontSize = 11.sp
                )
                Spacer(modifier = Modifier.height(6.dp))
                Text(
                    text = if (textBuffer.isEmpty()) "Tapez sur les touches ci-dessous..." else textBuffer,
                    color = if (textBuffer.isEmpty()) Color.White.copy(alpha = 0.3f) else SecondaryCyan,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.SemiBold
                )
            }
        }

        Spacer(modifier = Modifier.weight(1f))

        // Virtual Keyboard Keys Layout
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .background(DarkNavy)
                .padding(horizontal = 8.dp, vertical = 14.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            // Function & Modifiers row
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(6.dp)
            ) {
                row1.forEach { key ->
                    KeyCap(
                        label = key,
                        modifier = Modifier.weight(1f),
                        isModifier = true,
                        onClick = {
                            remoteClient.sendKeyEvent(RemoteKeyEvent(key, isModifier = true))
                        }
                    )
                }
            }

            // Row 2
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(4.dp)
            ) {
                row2.forEach { key ->
                    KeyCap(
                        label = key,
                        modifier = Modifier.weight(1f),
                        onClick = {
                            textBuffer += key
                            remoteClient.sendKeyEvent(RemoteKeyEvent(key))
                        }
                    )
                }
            }

            // Row 3
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(4.dp)
            ) {
                row3.forEach { key ->
                    KeyCap(
                        label = key,
                        modifier = Modifier.weight(1f),
                        onClick = {
                            textBuffer += key
                            remoteClient.sendKeyEvent(RemoteKeyEvent(key))
                        }
                    )
                }
            }

            // Row 4
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(4.dp)
            ) {
                row4.forEach { key ->
                    val isBack = key == "⌫"
                    KeyCap(
                        label = key,
                        modifier = Modifier.weight(if (key == "Shift" || isBack) 1.5f else 1f),
                        isModifier = key == "Shift" || isBack,
                        onClick = {
                            if (isBack) {
                                if (textBuffer.isNotEmpty()) textBuffer = textBuffer.dropLast(1)
                                remoteClient.sendKeyEvent(RemoteKeyEvent("Backspace", isSpecial = true))
                            } else {
                                remoteClient.sendKeyEvent(RemoteKeyEvent(key))
                            }
                        }
                    )
                }
            }

            // Bottom Space & Enter Row
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(6.dp)
            ) {
                KeyCap(
                    label = "Espace",
                    modifier = Modifier.weight(3f),
                    onClick = {
                        textBuffer += " "
                        remoteClient.sendKeyEvent(RemoteKeyEvent("Space"))
                    }
                )
                KeyCap(
                    label = "Entrée ↵",
                    modifier = Modifier.weight(1.5f),
                    isAccent = true,
                    onClick = {
                        textBuffer += "\n"
                        remoteClient.sendKeyEvent(RemoteKeyEvent("Enter", isSpecial = true))
                    }
                )
            }
        }
    }
}

@Composable
private fun KeyCap(
    label: String,
    modifier: Modifier = Modifier,
    isModifier: Boolean = false,
    isAccent: Boolean = false,
    onClick: () -> Unit
) {
    val bgColor = when {
        isAccent -> PrimaryBlue
        isModifier -> Color(0xFF334155)
        else -> Color(0xFF1E293B)
    }

    Box(
        modifier = modifier
            .height(42.dp)
            .clip(RoundedCornerShape(6.dp))
            .background(bgColor)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center
    ) {
        Text(
            text = label,
            color = Color.White,
            fontSize = if (label.length > 2) 10.sp else 13.sp,
            fontWeight = FontWeight.SemiBold
        )
    }
}
