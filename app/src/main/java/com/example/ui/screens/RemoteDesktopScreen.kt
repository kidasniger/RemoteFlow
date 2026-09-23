package com.example.ui.screens

import android.graphics.BitmapFactory
import android.util.Base64
import androidx.compose.foundation.Image
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
import androidx.compose.runtime.DisposableEffect
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.input.pointer.pointerInput
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
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlin.math.roundToInt

@Composable
fun RemoteDesktopScreen(
    remoteClient: RemotePcClient,
    onBackClick: () -> Unit,
    onOpenKeyboard: () -> Unit
) {
    val state by remoteClient.connectionState.collectAsState()
    val isConnected = state is ConnectionState.Connected
    val screenFrame by remoteClient.screenFrame.collectAsState()

    var isFullscreen by remember { mutableStateOf(false) }
    var cursorX by remember { mutableFloatStateOf(300f) }
    var cursorY by remember { mutableFloatStateOf(300f) }
    var feedback by remember { mutableStateOf("") }
    var screenBitmap by remember { mutableStateOf<ImageBitmap?>(null) }

    LaunchedEffect(isConnected) {
        if (isConnected) {
            remoteClient.startScreenStream(8, 1280, 60, -1)
        } else {
            remoteClient.stopScreenStream()
        }
    }

    DisposableEffect(Unit) {
        onDispose { remoteClient.stopScreenStream() }
    }

    LaunchedEffect(screenFrame?.sequence) {
        val frame = screenFrame ?: return@LaunchedEffect
        if (!frame.format.equals("jpeg", true)) return@LaunchedEffect

        screenBitmap = withContext(Dispatchers.Default) {
            try {
                val bytes = Base64.decode(frame.dataBase64, Base64.DEFAULT)
                BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
            } catch (_: Exception) {
                null
            }
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(DarkSlate)
    ) {
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
                            tint = Color.White
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

                Text(
                    text = if (isConnected) {
                        "TLS • " + (state as ConnectionState.Connected).device.latencyMs + " ms"
                    } else {
                        "Non connecté"
                    },
                    color = Color.White.copy(alpha = 0.7f),
                    fontSize = 11.sp
                )
            }
        }

        Box(
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth()
                .background(CardDark)
                .pointerInput(Unit) {
                    detectDragGestures { change, amount ->
                        change.consume()
                        cursorX += amount.x
                        cursorY += amount.y
                        remoteClient.sendMouseEvent(
                            RemoteMouseEvent(
                                type = MouseAction.MOVE_RELATIVE,
                                deltaX = amount.x,
                                deltaY = amount.y
                            )
                        )
                    }
                }
                .pointerInput(Unit) {
                    detectTapGestures(
                        onTap = {
                            feedback = "Clic Gauche"
                            remoteClient.sendMouseEvent(
                                RemoteMouseEvent(MouseAction.LEFT_CLICK)
                            )
                        },
                        onDoubleTap = {
                            feedback = "Double Clic"
                            remoteClient.sendMouseEvent(
                                RemoteMouseEvent(MouseAction.DOUBLE_CLICK)
                            )
                        },
                        onLongPress = {
                            feedback = "Clic Droit"
                            remoteClient.sendMouseEvent(
                                RemoteMouseEvent(MouseAction.RIGHT_CLICK)
                            )
                        }
                    )
                }
        ) {
            if (screenBitmap != null) {
                Image(
                    bitmap = screenBitmap!!,
                    contentDescription = "Écran Windows",
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(10.dp)
                        .clip(RoundedCornerShape(12.dp)),
                    contentScale = ContentScale.Fit
                )
                Box(
                    modifier = Modifier
                        .align(Alignment.TopStart)
                        .padding(16.dp)
                        .clip(RoundedCornerShape(10.dp))
                        .background(DarkNavy.copy(alpha = 0.72f))
                        .padding(horizontal = 9.dp, vertical = 5.dp)
                ) {
                    Text(
                        text = "ÉCRAN PC • " +
                            (screenFrame?.width ?: 0) + "×" +
                            (screenFrame?.height ?: 0),
                        color = Color.White,
                        fontSize = 9.sp,
                        fontWeight = FontWeight.Bold
                    )
                }
            } else {
                Column(
                    modifier = Modifier.align(Alignment.Center),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Icon(
                        imageVector = Icons.Default.Mouse,
                        contentDescription = null,
                        tint = Color.White.copy(alpha = 0.32f),
                        modifier = Modifier.size(48.dp)
                    )
                    Spacer(modifier = Modifier.height(10.dp))
                    Text(
                        text = if (isConnected) "Réception du bureau Windows…" else "Connectez d'abord le PC",
                        color = Color.White,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold
                    )
                }
            }

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

            if (feedback.isNotEmpty()) {
                Box(
                    modifier = Modifier
                        .align(Alignment.TopCenter)
                        .padding(top = 16.dp)
                        .clip(RoundedCornerShape(16.dp))
                        .background(PrimaryBlue.copy(alpha = 0.85f))
                        .padding(horizontal = 14.dp, vertical = 6.dp)
                ) {
                    Text(
                        text = feedback,
                        color = Color.White,
                        fontSize = 12.sp,
                        fontWeight = FontWeight.Bold
                    )
                }
            }

            Row(
                modifier = Modifier
                    .align(Alignment.BottomCenter)
                    .fillMaxWidth()
                    .height(60.dp)
                    .padding(horizontal = 12.dp, vertical = 8.dp),
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .fillMaxHeight()
                        .clip(RoundedCornerShape(8.dp))
                        .background(Color.White.copy(alpha = 0.08f))
                        .border(1.dp, Color.White.copy(alpha = 0.15f), RoundedCornerShape(8.dp))
                        .clickable {
                            feedback = "Clic Gauche"
                            remoteClient.sendMouseEvent(RemoteMouseEvent(MouseAction.LEFT_CLICK))
                        },
                    contentAlignment = Alignment.Center
                ) {
                    Text("CLIC GAUCHE", color = Color.White, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }

                Box(
                    modifier = Modifier
                        .weight(1f)
                        .fillMaxHeight()
                        .clip(RoundedCornerShape(8.dp))
                        .background(Color.White.copy(alpha = 0.08f))
                        .border(1.dp, Color.White.copy(alpha = 0.15f), RoundedCornerShape(8.dp))
                        .clickable {
                            feedback = "Clic Droit"
                            remoteClient.sendMouseEvent(RemoteMouseEvent(MouseAction.RIGHT_CLICK))
                        },
                    contentAlignment = Alignment.Center
                ) {
                    Text("CLIC DROIT", color = Color.White, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }
            }
        }

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
                    feedback = "Clic"
                    remoteClient.sendMouseEvent(RemoteMouseEvent(MouseAction.LEFT_CLICK))
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
                    feedback = "Scroll ↑"
                    remoteClient.sendMouseEvent(RemoteMouseEvent(MouseAction.SCROLL_UP))
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
    icon: androidx.compose.ui.graphics.vector.ImageVector,
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
