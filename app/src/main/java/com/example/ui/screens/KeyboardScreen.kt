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
    var modifiers by remember { mutableStateOf(setOf<String>()) }

    val modifierKeys = setOf("Ctrl", "Alt", "Win", "Shift")

    fun sendKey(key: String, special: Boolean = false) {
        val chord = if (modifiers.isEmpty()) null
        else (modifiers.toList() + key).joinToString("+")
        if (!special && key.length == 1) {
            textBuffer += key
        }
        if (key == "Space") {
            textBuffer += " "
        }
        remoteClient.sendKeyEvent(
            RemoteKeyEvent(
                keyLabel = key,
                isSpecial = special,
                chord = chord
            )
        )
        modifiers = emptySet()
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(DarkSlate)
            .testTag("keyboard_screen")
    ) {
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
                    tint = Color.White
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

        Box(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp)
                .height(80.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(DarkNavy)
                .padding(12.dp)
        ) {
            Column {
                Text(
                    text = if (modifiers.isEmpty()) {
                        "Frappe PC"
                    } else {
                        "Modificateurs : " + modifiers.joinToString(" + ")
                    },
                    color = if (modifiers.isEmpty()) {
                        Color.White.copy(alpha = 0.5f)
                    } else {
                        SecondaryCyan
                    },
                    fontSize = 11.sp
                )
                Spacer(modifier = Modifier.height(6.dp))
                Text(
                    text = if (textBuffer.isEmpty()) {
                        "Appuyez sur une touche…"
                    } else {
                        textBuffer.takeLast(120)
                    },
                    color = if (textBuffer.isEmpty()) {
                        Color.White.copy(alpha = 0.3f)
                    } else {
                        SecondaryCyan
                    },
                    fontSize = 14.sp,
                    fontWeight = FontWeight.SemiBold
                )
            }
        }

        Spacer(modifier = Modifier.weight(1f))

        Column(
            modifier = Modifier
                .fillMaxWidth()
                .background(DarkNavy)
                .padding(horizontal = 8.dp, vertical = 14.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            val firstRow = listOf("Esc", "Tab", "Ctrl", "Alt", "Win", "Del")
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(6.dp)
            ) {
                firstRow.forEach { key ->
                    val isModifier = key in modifierKeys
                    KeyCap(
                        label = key,
                        modifier = Modifier.weight(1f),
                        isModifier = isModifier,
                        selected = isModifier && modifiers.contains(key),
                        onClick = {
                            if (isModifier) {
                                modifiers = if (modifiers.contains(key)) {
                                    modifiers - key
                                } else {
                                    modifiers + key
                                }
                            } else {
                                sendKey(
                                    key,
                                    special = key == "Esc" || key == "Tab" || key == "Del"
                                )
                            }
                        }
                    )
                }
            }

            listOf(
                listOf("Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P"),
                listOf("A", "S", "D", "F", "G", "H", "J", "K", "L")
            ).forEach { row ->
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(4.dp)
                ) {
                    row.forEach { key ->
                        KeyCap(
                            label = key,
                            modifier = Modifier.weight(1f),
                            onClick = { sendKey(key) }
                        )
                    }
                }
            }

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(4.dp)
            ) {
                KeyCap(
                    label = "Shift",
                    modifier = Modifier.weight(1.5f),
                    isModifier = true,
                    selected = modifiers.contains("Shift"),
                    onClick = {
                        modifiers = if (modifiers.contains("Shift")) {
                            modifiers - "Shift"
                        } else {
                            modifiers + "Shift"
                        }
                    }
                )
                listOf("Z", "X", "C", "V", "B", "N", "M").forEach { key ->
                    KeyCap(
                        label = key,
                        modifier = Modifier.weight(1f),
                        onClick = { sendKey(key) }
                    )
                }
                KeyCap(
                    label = "⌫",
                    modifier = Modifier.weight(1.5f),
                    onClick = {
                        if (textBuffer.isNotEmpty()) textBuffer = textBuffer.dropLast(1)
                        sendKey("Backspace", special = true)
                    }
                )
            }

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(6.dp)
            ) {
                KeyCap(
                    label = "Espace",
                    modifier = Modifier.weight(3f),
                    onClick = { sendKey("Space", special = true) }
                )
                KeyCap(
                    label = "Entrée ↵",
                    modifier = Modifier.weight(1.5f),
                    isAccent = true,
                    onClick = {
                        textBuffer += "
"
                        sendKey("Enter", special = true)
                    }
                )
            }
        }
    }
}

@Composable
private fun KeyCap(
    label: String,
    modifier: Modifier,
    isModifier: Boolean = false,
    selected: Boolean = false,
    isAccent: Boolean = false,
    onClick: () -> Unit
) {
    val backgroundColor = when {
        isAccent -> PrimaryBlue
        selected -> SecondaryCyan.copy(alpha = 0.65f)
        isModifier -> Color(0xFF334155)
        else -> Color(0xFF1E293B)
    }

    Box(
        modifier = modifier
            .height(42.dp)
            .clip(RoundedCornerShape(6.dp))
            .background(backgroundColor)
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
