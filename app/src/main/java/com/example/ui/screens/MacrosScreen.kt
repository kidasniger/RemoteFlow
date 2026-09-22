package com.example.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.VolumeOff
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.CameraAlt
import androidx.compose.material.icons.filled.Code
import androidx.compose.material.icons.filled.FlashOn
import androidx.compose.material.icons.filled.Language
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.PowerSettingsNew
import androidx.compose.material.icons.filled.Star
import androidx.compose.material.icons.filled.VolumeOff
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedTextField
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
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.macros.MacroIconType
import com.example.macros.MacroItem
import com.example.macros.MacroRepository
import com.example.ui.theme.BorderLight
import com.example.ui.theme.PrimaryBlue
import com.example.ui.theme.SecondaryCyan
import com.example.ui.theme.SurfaceLight
import com.example.ui.theme.TextMuted
import com.example.ui.theme.TextPrimary
import com.example.ui.theme.TextSecondary

@Composable
fun MacrosScreen(
    macroRepository: MacroRepository,
    onBackClick: () -> Unit
) {
    val macros by macroRepository.macros.collectAsState()
    val lastTriggered by macroRepository.lastTriggeredMacro.collectAsState()
    var showAddDialog by remember { mutableStateOf(false) }

    var newLabel by remember { mutableStateOf("") }
    var newCommand by remember { mutableStateOf("") }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(SurfaceLight)
            .testTag("macros_screen")
    ) {
        // Top Header
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .height(56.dp)
                .background(Color.White)
                .border(1.dp, BorderLight)
                .padding(horizontal = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
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
                    text = "Macros Personnalisées",
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold,
                    color = TextPrimary
                )
            }

            // "+" Button
            Box(
                modifier = Modifier
                    .size(32.dp)
                    .clip(CircleShape)
                    .background(
                        Brush.linearGradient(listOf(PrimaryBlue, SecondaryCyan))
                    )
                    .clickable { showAddDialog = true }
                    .testTag("add_macro_button"),
                contentAlignment = Alignment.Center
            ) {
                Icon(
                    imageVector = Icons.Default.Add,
                    contentDescription = "Ajouter",
                    tint = Color.White,
                    modifier = Modifier.size(18.dp)
                )
            }
        }

        // Execution feedback banner
        if (lastTriggered != null) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .background(SecondaryCyan.copy(alpha = 0.15f))
                    .padding(horizontal = 16.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                Text(
                    text = "Commande déclenchée : $lastTriggered",
                    fontSize = 11.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = PrimaryBlue
                )
                Text(
                    text = "Effacer",
                    fontSize = 10.sp,
                    color = TextMuted,
                    modifier = Modifier.clickable { macroRepository.clearFeedback() }
                )
            }
        }

        // 3x3 Grid of Macro Cards
        LazyVerticalGrid(
            columns = GridCells.Fixed(3),
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth()
                .padding(horizontal = 12.dp),
            contentPadding = PaddingValues(vertical = 12.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            items(macros) { macro ->
                val baseColor = Color(macro.colorHex)
                val icon = getMacroIcon(macro.iconType)

                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .aspectRatio(1f)
                        .shadow(
                            elevation = 6.dp,
                            shape = RoundedCornerShape(18.dp),
                            spotColor = baseColor.copy(alpha = 0.4f)
                        )
                        .clip(RoundedCornerShape(18.dp))
                        .background(
                            Brush.linearGradient(
                                listOf(baseColor, baseColor.copy(alpha = 0.75f))
                            )
                        )
                        .clickable { macroRepository.triggerMacro(macro) }
                        .padding(10.dp)
                        .testTag("macro_item_${macro.id}"),
                    contentAlignment = Alignment.TopStart
                ) {
                    Column(
                        modifier = Modifier.fillMaxSize(),
                        verticalArrangement = Arrangement.SpaceBetween
                    ) {
                        Icon(
                            imageVector = icon,
                            contentDescription = macro.label,
                            tint = Color.White,
                            modifier = Modifier.size(20.dp)
                        )

                        Text(
                            text = macro.label,
                            color = Color.White,
                            fontSize = 10.5.sp,
                            fontWeight = FontWeight.Bold,
                            lineHeight = 13.sp,
                            maxLines = 2
                        )
                    }
                }
            }
        }
    }

    if (showAddDialog) {
        AlertDialog(
            onDismissRequest = { showAddDialog = false },
            title = { Text("Nouvelle Macro", fontWeight = FontWeight.Bold) },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    Text("Créez un raccourci ou script pour votre PC :", fontSize = 12.sp, color = TextSecondary)
                    OutlinedTextField(
                        value = newLabel,
                        onValueChange = { newLabel = it },
                        label = { Text("Nom de la macro") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = newCommand,
                        onValueChange = { newCommand = it },
                        label = { Text("Commande / Raccourci") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                }
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        if (newLabel.isNotBlank()) {
                            macroRepository.addCustomMacro(
                                label = newLabel,
                                command = if (newCommand.isNotBlank()) newCommand else "echo $newLabel",
                                colorHex = 0xFF005CFF
                            )
                            newLabel = ""
                            newCommand = ""
                            showAddDialog = false
                        }
                    }
                ) {
                    Text("Créer", color = PrimaryBlue, fontWeight = FontWeight.Bold)
                }
            },
            dismissButton = {
                TextButton(onClick = { showAddDialog = false }) {
                    Text("Annuler", color = TextMuted)
                }
            }
        )
    }
}

private fun getMacroIcon(type: MacroIconType): ImageVector {
    return when (type) {
        MacroIconType.POWER -> Icons.Default.PowerSettingsNew
        MacroIconType.LOCK -> Icons.Default.Lock
        MacroIconType.PLAY -> Icons.Default.PlayArrow
        MacroIconType.MUTE -> Icons.AutoMirrored.Filled.VolumeOff
        MacroIconType.SCRIPT -> Icons.Default.Code
        MacroIconType.CAPTURE -> Icons.Default.CameraAlt
        MacroIconType.BROWSER -> Icons.Default.Language
        MacroIconType.BOOST -> Icons.Default.FlashOn
        MacroIconType.CUSTOM -> Icons.Default.Star
    }
}
