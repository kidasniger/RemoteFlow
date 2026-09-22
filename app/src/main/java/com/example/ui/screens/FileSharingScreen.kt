package com.example.ui.screens

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Folder
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.R
import com.example.files.FileManager
import com.example.files.SharedFile
import com.example.ui.theme.BgLightBlue
import com.example.ui.theme.BorderLight
import com.example.ui.theme.BorderSubtle
import com.example.ui.theme.DarkSlate
import com.example.ui.theme.PrimaryBlue
import com.example.ui.theme.SecondaryCyan
import com.example.ui.theme.StatusConnected
import com.example.ui.theme.SurfaceLight
import com.example.ui.theme.TextMuted
import com.example.ui.theme.TextPrimary
import com.example.ui.theme.TextSecondary

@Composable
fun FileSharingScreen(
    fileManager: FileManager,
    onBackClick: () -> Unit
) {
    val phoneFiles by fileManager.phoneFiles.collectAsState()
    val pcFiles by fileManager.pcFiles.collectAsState()
    val selectedFiles by fileManager.selectedFiles.collectAsState()
    val transferProgress by fileManager.transferProgress.collectAsState()
    val isTransferring by fileManager.isTransferring.collectAsState()

    var activeTab by remember { mutableStateOf("phone") } // "phone" or "pc"
    var transferStatusMsg by remember { mutableStateOf<String?>(null) }

    val filePickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.GetMultipleContents()
    ) { uris: List<Uri> ->
        if (uris.isNotEmpty()) {
            fileManager.addFilesFromUris(uris)
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.White)
            .testTag("file_sharing_screen")
    ) {
        // Top Header
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .height(56.dp)
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
                Image(
                    painter = painterResource(id = R.drawable.remoteflow_app_icon_1790042238116),
                    contentDescription = null,
                    contentScale = ContentScale.Crop,
                    modifier = Modifier
                        .size(26.dp)
                        .clip(RoundedCornerShape(6.dp))
                )
                Spacer(modifier = Modifier.width(8.dp))
                Text(
                    text = "Partage",
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold,
                    color = TextPrimary
                )
            }

            // Tab Switch (Téléphone / PC)
            Row(
                modifier = Modifier
                    .clip(RoundedCornerShape(20.dp))
                    .background(BorderLight)
                    .padding(3.dp)
            ) {
                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(16.dp))
                        .background(if (activeTab == "phone") DarkSlate else Color.Transparent)
                        .clickable { activeTab = "phone" }
                        .padding(horizontal = 12.dp, vertical = 6.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        text = "Téléphone",
                        color = if (activeTab == "phone") Color.White else TextSecondary,
                        fontSize = 10.5.sp,
                        fontWeight = FontWeight.SemiBold
                    )
                }
                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(16.dp))
                        .background(if (activeTab == "pc") DarkSlate else Color.Transparent)
                        .clickable { activeTab = "pc" }
                        .padding(horizontal = 12.dp, vertical = 6.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        text = "PC",
                        color = if (activeTab == "pc") Color.White else TextSecondary,
                        fontSize = 10.5.sp,
                        fontWeight = FontWeight.SemiBold
                    )
                }
            }
        }

        // Add File Bar
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 14.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Text(
                text = if (activeTab == "phone") "Fichiers sur ce téléphone" else "Fichiers distants sur PC",
                fontSize = 12.sp,
                fontWeight = FontWeight.Bold,
                color = TextPrimary
            )

            Row(
                verticalAlignment = Alignment.CenterVertically,
                modifier = Modifier
                    .clip(RoundedCornerShape(14.dp))
                    .background(BgLightBlue)
                    .clickable { filePickerLauncher.launch("*/*") }
                    .padding(horizontal = 10.dp, vertical = 6.dp)
            ) {
                Icon(
                    imageVector = Icons.Default.Add,
                    contentDescription = "Ajouter",
                    tint = PrimaryBlue,
                    modifier = Modifier.size(14.dp)
                )
                Spacer(modifier = Modifier.width(4.dp))
                Text(
                    text = "Ajouter",
                    fontSize = 11.sp,
                    color = PrimaryBlue,
                    fontWeight = FontWeight.SemiBold
                )
            }
        }

        // Transfer Progress Indicator
        if (isTransferring) {
            Column(modifier = Modifier.padding(horizontal = 14.dp, vertical = 6.dp)) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Text("Transfert en cours...", fontSize = 11.sp, color = PrimaryBlue, fontWeight = FontWeight.SemiBold)
                    Text("${(transferProgress * 100).toInt()}%", fontSize = 11.sp, color = TextSecondary)
                }
                Spacer(modifier = Modifier.height(4.dp))
                LinearProgressIndicator(
                    progress = { transferProgress },
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(6.dp)
                        .clip(RoundedCornerShape(3.dp)),
                    color = PrimaryBlue,
                    trackColor = BorderLight
                )
            }
        }

        // Split Browser View
        Row(
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth()
        ) {
            // Left Phone Files Pane
            Box(
                modifier = Modifier
                    .weight(1f)
                    .fillMaxHeight()
                    .padding(8.dp)
            ) {
                if (phoneFiles.isEmpty()) {
                    Text(
                        text = "Aucun fichier.\nCliquez sur '+ Ajouter' pour en importer.",
                        fontSize = 10.sp,
                        color = TextMuted,
                        modifier = Modifier.padding(8.dp)
                    )
                } else {
                    LazyColumn(
                        modifier = Modifier.fillMaxSize(),
                        verticalArrangement = Arrangement.spacedBy(6.dp)
                    ) {
                        items(phoneFiles) { file ->
                            val isSelected = selectedFiles.contains(file.id)
                            FileItemRow(
                                file = file,
                                isSelected = isSelected,
                                onToggle = { fileManager.toggleFileSelection(file.id) }
                            )
                        }
                    }
                }
            }

            // Middle Action Buttons Column
            Column(
                modifier = Modifier
                    .width(76.dp)
                    .fillMaxHeight()
                    .background(SurfaceLight)
                    .border(width = 1.dp, color = BorderLight)
                    .padding(vertical = 16.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center
            ) {
                // Send button
                Box(
                    modifier = Modifier
                        .size(56.dp, 34.dp)
                        .clip(RoundedCornerShape(17.dp))
                        .background(
                            Brush.linearGradient(listOf(PrimaryBlue, SecondaryCyan))
                        )
                        .clickable {
                            fileManager.sendSelectedFilesToPc { success, msg ->
                                transferStatusMsg = msg
                            }
                        }
                        .testTag("send_files_button"),
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        text = "→ Envoyer",
                        color = Color.White,
                        fontSize = 9.sp,
                        fontWeight = FontWeight.Bold
                    )
                }

                Spacer(modifier = Modifier.height(14.dp))

                // Receive button
                Box(
                    modifier = Modifier
                        .size(56.dp, 34.dp)
                        .clip(RoundedCornerShape(17.dp))
                        .background(Color.White)
                        .border(1.dp, BorderSubtle, RoundedCornerShape(17.dp))
                        .clickable {
                            fileManager.receiveSelectedFilesFromPc { success, msg ->
                                transferStatusMsg = msg
                            }
                        }
                        .testTag("receive_files_button"),
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        text = "← Recevoir",
                        color = DarkSlate,
                        fontSize = 9.sp,
                        fontWeight = FontWeight.Bold
                    )
                }
            }

            // Right PC Files Pane
            Box(
                modifier = Modifier
                    .weight(1f)
                    .fillMaxHeight()
                    .padding(8.dp)
            ) {
                if (pcFiles.isEmpty()) {
                    Text(
                        text = "Aucun fichier reçu du PC.",
                        fontSize = 10.sp,
                        color = TextMuted,
                        modifier = Modifier.padding(8.dp)
                    )
                } else {
                    LazyColumn(
                        modifier = Modifier.fillMaxSize(),
                        verticalArrangement = Arrangement.spacedBy(6.dp)
                    ) {
                        items(pcFiles) { file ->
                            val isSelected = selectedFiles.contains(file.id)
                            FileItemRow(
                                file = file,
                                isSelected = isSelected,
                                onToggle = { fileManager.toggleFileSelection(file.id) }
                            )
                        }
                    }
                }
            }
        }

        // Bottom Summary Bar
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .height(44.dp)
                .background(SurfaceLight)
                .border(1.dp, BorderLight)
                .padding(horizontal = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Text(
                text = "${selectedFiles.size} fichier(s) sélectionné(s)",
                fontSize = 11.sp,
                color = TextSecondary,
                fontWeight = FontWeight.Medium
            )

            if (transferStatusMsg != null) {
                Text(
                    text = transferStatusMsg ?: "",
                    fontSize = 11.sp,
                    color = StatusConnected,
                    fontWeight = FontWeight.Bold
                )
            }
        }
    }
}

@Composable
private fun FileItemRow(
    file: SharedFile,
    isSelected: Boolean,
    onToggle: () -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .background(if (isSelected) BgLightBlue else SurfaceLight)
            .border(
                width = 1.dp,
                color = if (isSelected) PrimaryBlue.copy(alpha = 0.4f) else BorderLight,
                shape = RoundedCornerShape(12.dp)
            )
            .clickable(onClick = onToggle)
            .padding(8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(
            modifier = Modifier
                .size(28.dp)
                .clip(RoundedCornerShape(6.dp))
                .background(Color.White)
                .border(1.dp, BorderLight, RoundedCornerShape(6.dp)),
            contentAlignment = Alignment.Center
        ) {
            Icon(
                imageVector = if (file.isLocal) Icons.Default.Description else Icons.Default.Folder,
                contentDescription = null,
                tint = if (file.isLocal) PrimaryBlue else SecondaryCyan,
                modifier = Modifier.size(16.dp)
            )
        }

        Spacer(modifier = Modifier.width(6.dp))

        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = file.name,
                fontSize = 10.sp,
                fontWeight = FontWeight.SemiBold,
                color = TextPrimary,
                maxLines = 1
            )
            Text(
                text = file.formattedSize,
                fontSize = 8.5.sp,
                color = TextMuted
            )
        }
    }
}
