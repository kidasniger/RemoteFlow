package com.example.ui.screens

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Fullscreen
import androidx.compose.material.icons.filled.FullscreenExit
import androidx.compose.material.icons.filled.Keyboard
import androidx.compose.material.icons.filled.Mouse
import androidx.compose.material.icons.filled.SwapVert
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.domain.model.ConnectionState
import com.example.network.MouseAction
import com.example.network.RemoteMouseEvent
import com.example.network.RemotePcClient
import com.example.ui.theme.CardDark
import com.example.ui.theme.DarkNavy
import com.example.ui.theme.DarkSlate
import com.example.ui.theme.PrimaryBlue
import com.example.ui.theme.SecondaryCyan
import com.example.ui.theme.StatusConnected
import kotlinx.coroutines.delay
import kotlin.math.roundToInt

@Composable
fun RemoteDesktopScreen(
    remoteClient: RemotePcClient,
    onBackClick: () -> Unit,
    onOpenKeyboard: () -> Unit
) {
    val connectionState by remoteClient.connectionState.collectAsState()
    val isConnected = connectionState is ConnectionState.Connected
    val device = (connectionState as? ConnectionState.Connected)?.device

    var isFullscreen by remember { mutableStateOf(false) }
    var cursorX by remember { mutableFloatStateOf(300f) }
    var cursorY by remember { mutableFloatStateOf(400f) }
    var clickFeedbackText by remember { mutableStateOf("") }

    LaunchedEffect(clickFeedbackText) {
        if (clickFeedbackText.isNotEmpty()) {
            delay(800)
            clickFeedbackText = ""
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(DarkSlate)
            .testTag("remote_desktop_screen")
    ) {
        // Top Toolbar
        if (!isFullscreen) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(52.dp)
                    .background(DarkNavy)
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
                            tint = Color.White,
                            modifier = Modifier.size(20.dp)
                        )
                    }
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(
                        text = "Bureau à Distance",
                        color = Color.White,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold
                    )
                }

                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(6.dp)
                ) {
                    Box(
                        modifier = Modifier
                            .size(8.dp)
                            .clip(CircleShape)
                            .background(if (isConnected) StatusConnected else Color.Gray)
                    )
                    Text(
                        text = if (isConnected && device != null) "${device.latencyMs} ms • ${device.ipAddress}" else "Non connecté",
                        color = Color.White.copy(alpha = 0.7f),
                        fontSize = 11.sp,
                        fontWeight = FontWeight.Medium
                    )
                }
            }
        }

        // Remote Trackpad & Display Area
        Box(
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth()
                .background(CardDark)
                .pointerInput(Unit) {
                    detectDragGestures { change, dragAmount ->
                        change.consume()
                        cursorX = (cursorX + dragAmount.x).coerceIn(20f, 1000f)
                        cursorY = (cursorY + dragAmount.y).coerceIn(20f, 1600f)
                        remoteClient.sendMouseEvent(
                            RemoteMouseEvent(
                                type = MouseAction.MOVE,
                                deltaX = dragAmount.x,
                                deltaY = dragAmount.y
                            )
                        )
                    }
                }
                .pointerInput(Unit) {
                    detectTapGestures(
                        onTap = {
                            clickFeedbackText = "Clic Gauche"
                            remoteClient.sendMouseEvent(
                                RemoteMouseEvent(type = MouseAction.LEFT_CLICK, x = cursorX, y = cursorY)
                            )
                        },
                        onDoubleTap = {
                            clickFeedbackText = "Double Clic"
                            remoteClient.sendMouseEvent(
                                RemoteMouseEvent(type = MouseAction.DOUBLE_CLICK, x = cursorX, y = cursorY)
                            )
                        },
                        onLongPress = {
                            clickFeedbackText = "Clic Droit"
                            remoteClient.sendMouseEvent(
                                RemoteMouseEvent(type = MouseAction.RIGHT_CLICK, x = cursorX, y = cursorY)
                            )
                        }
                    )
                }
        ) {
            if (!isConnected) {
                // Disconnected guidance
                Column(
                    modifier = Modifier
                        .align(Alignment.Center)
                        .padding(24.dp),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Icon(
                        imageVector = Icons.Default.Mouse,
                        contentDescription = "Trackpad",
                        tint = Color.White.copy(alpha = 0.3f),
                        modifier = Modifier.size(48.dp)
                    )
                    Spacer(modifier = Modifier.height(12.dp))
                    Text(
                        text = "Pavé Tactile PC Haute Précision",
                        color = Color.White,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold
                    )
                    Spacer(modifier = Modifier.height(6.dp))
                    Text(
                        text = "Glissez pour diriger le curseur, touchez pour cliquer.\nAssociez votre PC via l'écran Connexion.",
                        color = Color.White.copy(alpha = 0.6f),
                        fontSize = 12.sp,
                        lineHeight = 16.sp,
                        textAlign = androidx.compose.ui.text.style.TextAlign.Center
                    )
                }
            } else {
                // Connected Trackpad grid guide
                Column(
                    modifier = Modifier
                        .align(Alignment.Center)
                        .padding(24.dp),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Text(
                        text = "Surface Tactile Active",
                        color = SecondaryCyan.copy(alpha = 0.5f),
                        fontSize = 12.sp,
                        fontWeight = FontWeight.SemiBold
                    )
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        text = "1 doigt = Clic Gauche • 2 doigts = Défilement • Long appui = Clic Droit",
                        color = Color.White.copy(alpha = 0.4f),
                        fontSize = 10.sp
                    )
                }
            }

            // Real Virtual Cursor on Screen
            Box(
                modifier = Modifier
                    .offset { IntOffset(cursorX.roundToInt(), cursorY.roundToInt()) }
                    .size(22.dp)
            ) {
                Box(
                    modifier = Modifier
                        .size(14.dp)
                        .clip(CircleShape)
                        .background(SecondaryCyan)
                        .border(2.dp, Color.White, CircleShape)
                )
            }

            // Click Feedback Toast
            if (clickFeedbackText.isNotEmpty()) {
                Box(
                    modifier = Modifier
                        .align(Alignment.TopCenter)
                        .padding(top = 16.dp)
                        .clip(RoundedCornerShape(16.dp))
                        .background(PrimaryBlue.copy(alpha = 0.85f))
                        .padding(horizontal = 14.dp, vertical = 6.dp)
                ) {
                    Text(
                        text = clickFeedbackText,
                        color = Color.White,
                        fontSize = 12.sp,
                        fontWeight = FontWeight.Bold
                    )
                }
            }

            // Dual Physical Click Buttons at the bottom of the trackpad
            Row(
                modifier = Modifier
                    .align(Alignment.BottomCenter)
                    .fillMaxWidth()
                    .height(60.dp)
                    .padding(horizontal = 12.dp, vertical = 8.dp),
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                // Left Click Button
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .fillMaxHeight()
                        .clip(RoundedCornerShape(8.dp))
                        .background(Color.White.copy(alpha = 0.08f))
                        .border(1.dp, Color.White.copy(alpha = 0.15f), RoundedCornerShape(8.dp))
                        .clickable {
                            clickFeedbackText = "Clic Gauche"
                            remoteClient.sendMouseEvent(
                                RemoteMouseEvent(type = MouseAction.LEFT_CLICK, x = cursorX, y = cursorY)
                            )
                        }
                        .testTag("left_click_button"),
                    contentAlignment = Alignment.Center
                ) {
                    Text("CLIC GAUCHE", color = Color.White, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }

                // Right Click Button
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .fillMaxHeight()
                        .clip(RoundedCornerShape(8.dp))
                        .background(Color.White.copy(alpha = 0.08f))
                        .border(1.dp, Color.White.copy(alpha = 0.15f), RoundedCornerShape(8.dp))
                        .clickable {
                            clickFeedbackText = "Clic Droit"
                            remoteClient.sendMouseEvent(
                                RemoteMouseEvent(type = MouseAction.RIGHT_CLICK, x = cursorX, y = cursorY)
                            )
                        }
                        .testTag("right_click_button"),
                    contentAlignment = Alignment.Center
                ) {
                    Text("CLIC DROIT", color = Color.White, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }
            }
        }

        // Bottom Dock
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .height(64.dp)
                .background(DarkNavy)
                .padding(horizontal = 16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceAround
        ) {
            DockActionButton(
                label = "Clic",
                icon = Icons.Default.Mouse,
                isSelected = true,
                onClick = {
                    clickFeedbackText = "Clic"
                    remoteClient.sendMouseEvent(
                        RemoteMouseEvent(type = MouseAction.LEFT_CLICK, x = cursorX, y = cursorY)
                    )
                }
            )

            DockActionButton(
                label = "Clavier",
                icon = Icons.Default.Keyboard,
                onClick = onOpenKeyboard
            )

            DockActionButton(
                label = "Scroll Haut",
                icon = Icons.Default.SwapVert,
                onClick = {
                    clickFeedbackText = "Scroll ↑"
                    remoteClient.sendMouseEvent(
                        RemoteMouseEvent(type = MouseAction.SCROLL_UP)
                    )
                }
            )

            DockActionButton(
                label = if (isFullscreen) "Réduire" else "Plein Écran",
                icon = if (isFullscreen) Icons.Default.FullscreenExit else Icons.Default.Fullscreen,
                onClick = { isFullscreen = !isFullscreen }
            )
        }
    }
}

@Composable
private fun DockActionButton(
    label: String,
    icon: ImageVector,
    isSelected: Boolean = false,
    onClick: () -> Unit
) {
    Column(
        modifier = Modifier
            .clip(RoundedCornerShape(8.dp))
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 6.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Icon(
            imageVector = icon,
            contentDescription = label,
            tint = if (isSelected) SecondaryCyan else Color.White.copy(alpha = 0.7f),
            modifier = Modifier.size(20.dp)
        )
        Spacer(modifier = Modifier.height(4.dp))
        Text(
            text = label,
            fontSize = 10.sp,
            color = if (isSelected) SecondaryCyan else Color.White.copy(alpha = 0.7f),
            fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal
        )
    }
}
