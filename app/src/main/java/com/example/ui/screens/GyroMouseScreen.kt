package com.example.ui.screens

import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Image
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
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.Icon
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.draw.scale
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.R
import com.example.network.MouseAction
import com.example.network.RemoteMouseEvent
import com.example.network.RemotePcClient
import com.example.sensors.GyroscopeManager
import com.example.ui.theme.DarkNavy
import com.example.ui.theme.PrimaryBlue
import com.example.ui.theme.SecondaryCyan

@Composable
fun GyroMouseScreen(
    gyroManager: GyroscopeManager,
    remoteClient: RemotePcClient,
    onBackClick: () -> Unit
) {
    val isActive by gyroManager.isActive.collectAsState()
    val sensitivity by gyroManager.sensitivity.collectAsState()
    val deltaMovement by gyroManager.deltaMovement.collectAsState()

    var clickFeedback by remember { mutableStateOf<String?>(null) }

    DisposableEffect(Unit) {
        onDispose {
            gyroManager.stop()
        }
    }

    // Send gyro movements to remote client when active
    if (isActive && (deltaMovement.first != 0f || deltaMovement.second != 0f)) {
        remoteClient.sendMouseEvent(
            RemoteMouseEvent(
                type = MouseAction.MOVE,
                deltaX = deltaMovement.first,
                deltaY = deltaMovement.second
            )
        )
    }

    val infiniteTransition = rememberInfiniteTransition(label = "pulse_radar")
    val pulseRadar by infiniteTransition.animateFloat(
        initialValue = 0.8f,
        targetValue = 1.35f,
        animationSpec = infiniteRepeatable(
            animation = tween(1200),
            repeatMode = RepeatMode.Restart
        ),
        label = "radar_scale"
    )

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(DarkNavy)
            .testTag("gyro_mouse_screen"),
        verticalArrangement = Arrangement.SpaceBetween
    ) {
        // Top Header
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .height(56.dp)
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
                    text = "Souris Gyroscopique",
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color.White
                )
            }

            // Calibrate button
            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(14.dp))
                    .background(Color.White.copy(alpha = 0.12f))
                    .clickable {
                        gyroManager.calibrate()
                        clickFeedback = "Calibré"
                    }
                    .padding(horizontal = 12.dp, vertical = 6.dp),
                contentAlignment = Alignment.Center
            ) {
                Text("Calibrer", color = Color.White, fontSize = 11.sp, fontWeight = FontWeight.Medium)
            }
        }

        // Center Graphic & Activator
        Column(
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth(),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center
        ) {
            // Floating 3D Air Mouse Card Visual
            Box(
                modifier = Modifier
                    .size(160.dp, 100.dp),
                contentAlignment = Alignment.Center
            ) {
                if (isActive) {
                    Box(
                        modifier = Modifier
                            .size(140.dp)
                            .scale(pulseRadar)
                            .clip(CircleShape)
                            .border(1.dp, SecondaryCyan.copy(alpha = 0.4f), CircleShape)
                    )
                }

                Box(
                    modifier = Modifier
                        .size(130.dp, 75.dp)
                        .rotate(-12f)
                        .shadow(16.dp, RoundedCornerShape(18.dp), spotColor = SecondaryCyan.copy(alpha = 0.3f))
                        .clip(RoundedCornerShape(18.dp))
                        .background(
                            Brush.linearGradient(
                                listOf(Color.White.copy(alpha = 0.15f), Color.White.copy(alpha = 0.05f))
                            )
                        )
                        .border(1.dp, Color.White.copy(alpha = 0.2f), RoundedCornerShape(18.dp)),
                    contentAlignment = Alignment.Center
                ) {
                    Box(
                        modifier = Modifier
                            .size(36.dp)
                            .clip(CircleShape)
                            .background(Color.White.copy(alpha = 0.12f))
                    )
                }
            }

            Spacer(modifier = Modifier.height(28.dp))

            // Main Activation Pill Button
            val buttonGradient = if (isActive) {
                Brush.horizontalGradient(listOf(SecondaryCyan, PrimaryBlue))
            } else {
                Brush.horizontalGradient(listOf(PrimaryBlue, SecondaryCyan))
            }

            Box(
                modifier = Modifier
                    .fillMaxWidth(0.78f)
                    .height(52.dp)
                    .shadow(
                        elevation = if (isActive) 16.dp else 8.dp,
                        shape = RoundedCornerShape(26.dp),
                        spotColor = SecondaryCyan.copy(alpha = 0.5f)
                    )
                    .clip(RoundedCornerShape(26.dp))
                    .background(buttonGradient)
                    .clickable {
                        if (isActive) {
                            gyroManager.stop()
                        } else {
                            val started = gyroManager.start()
                            if (!started) {
                                clickFeedback = "Gyroscope non détecté"
                            }
                        }
                    }
                    .testTag("air_mouse_toggle_button"),
                contentAlignment = Alignment.Center
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(
                        modifier = Modifier
                            .size(8.dp)
                            .clip(CircleShape)
                            .background(if (isActive) Color.White else Color.White.copy(alpha = 0.6f))
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(
                        text = if (isActive) "Air Mouse — DÉSACTIVER" else "Air Mouse — ACTIVER",
                        color = Color.White,
                        fontSize = 12.sp,
                        fontWeight = FontWeight.Bold,
                        letterSpacing = 1.sp
                    )
                }
            }

            if (!gyroManager.isSupported) {
                Spacer(modifier = Modifier.height(10.dp))
                Text(
                    text = "Mode tactile (Gyroscope matériel absent)",
                    color = Color.Yellow.copy(alpha = 0.8f),
                    fontSize = 11.sp
                )
            }

            if (clickFeedback != null) {
                Spacer(modifier = Modifier.height(8.dp))
                Text(
                    text = clickFeedback ?: "",
                    color = SecondaryCyan,
                    fontSize = 11.sp,
                    fontWeight = FontWeight.SemiBold
                )
            }
        }

        // Bottom Controls: Sensitivity Slider & Clic Buttons
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 24.dp, vertical = 20.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            // Sensitivity Slider
            Column {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Text(text = "Sensibilité", color = Color.White.copy(alpha = 0.7f), fontSize = 11.sp)
                    Text(
                        text = "${(sensitivity * 100).toInt()}%",
                        color = Color.White,
                        fontSize = 11.sp,
                        fontWeight = FontWeight.Bold
                    )
                }
                Slider(
                    value = sensitivity,
                    onValueChange = { gyroManager.setSensitivity(it) },
                    valueRange = 0.1f..1.5f,
                    colors = SliderDefaults.colors(
                        thumbColor = SecondaryCyan,
                        activeTrackColor = SecondaryCyan,
                        inactiveTrackColor = Color.White.copy(alpha = 0.2f)
                    ),
                    modifier = Modifier.height(30.dp)
                )
            }

            // Dual Clic Buttons (Large Touchpads)
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                // Left Click Button
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .height(48.dp)
                        .clip(RoundedCornerShape(24.dp))
                        .background(Color.White.copy(alpha = 0.12f))
                        .clickable {
                            clickFeedback = "Clic Gauche"
                            remoteClient.sendMouseEvent(RemoteMouseEvent(type = MouseAction.LEFT_CLICK))
                        }
                        .testTag("gyro_left_click_button"),
                    contentAlignment = Alignment.Center
                ) {
                    Text("Clic Gauche", color = Color.White, fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                }

                // Right Click Button
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .height(48.dp)
                        .clip(RoundedCornerShape(24.dp))
                        .background(Color.White.copy(alpha = 0.12f))
                        .clickable {
                            clickFeedback = "Clic Droit"
                            remoteClient.sendMouseEvent(RemoteMouseEvent(type = MouseAction.RIGHT_CLICK))
                        }
                        .testTag("gyro_right_click_button"),
                    contentAlignment = Alignment.Center
                ) {
                    Text("Clic Droit", color = Color.White, fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                }
            }
        }
    }
}
