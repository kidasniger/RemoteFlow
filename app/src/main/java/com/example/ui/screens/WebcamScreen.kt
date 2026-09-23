package com.example.ui.screens

import android.graphics.BitmapFactory
import android.util.Base64
import androidx.compose.foundation.Image
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
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.CameraAlt
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
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
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.domain.model.ConnectionState
import com.example.network.RemotePcClient
import com.example.ui.theme.DarkNavy
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

@Composable
fun WebcamScreen(
    remoteClient: RemotePcClient,
    onBackClick: () -> Unit
) {
    val connectionState by remoteClient.connectionState.collectAsState()
    val frame by remoteClient.webcamFrame.collectAsState()
    val connected = connectionState is ConnectionState.Connected

    var streaming by remember { mutableStateOf(true) }
    var bitmap by remember { mutableStateOf<ImageBitmap?>(null) }

    LaunchedEffect(connected, streaming) {
        if (connected && streaming) {
            remoteClient.startRemoteWebcam(
                cameraIndex = 0,
                width = 1280,
                height = 720,
                fps = 15,
                quality = 70
            )
        } else if (!streaming) {
            remoteClient.stopRemoteWebcam()
        }
    }

    DisposableEffect(Unit) {
        onDispose { remoteClient.stopRemoteWebcam() }
    }

    LaunchedEffect(frame?.sequence) {
        val current = frame ?: return@LaunchedEffect
        bitmap = withContext(Dispatchers.Default) {
            try {
                val bytes = Base64.decode(current.dataBase64, Base64.DEFAULT)
                BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
            } catch (_: Exception) {
                null
            }
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black)
    ) {
        Box(
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth()
        ) {
            if (bitmap != null) {
                Image(
                    bitmap = bitmap!!,
                    contentDescription = "Webcam du PC",
                    modifier = Modifier.fillMaxSize(),
                    contentScale = ContentScale.Fit
                )
            } else {
                Column(
                    modifier = Modifier.align(Alignment.Center),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Icon(
                        imageVector = Icons.Default.CameraAlt,
                        contentDescription = null,
                        tint = Color.White.copy(alpha = 0.35f),
                        modifier = Modifier.size(60.dp)
                    )
                    Spacer(modifier = Modifier.height(12.dp))
                    Text(
                        text = if (connected) "Réception de la webcam Windows…" else "Connectez le PC",
                        color = Color.White.copy(alpha = 0.75f),
                        fontSize = 13.sp
                    )
                }
            }

            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(14.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Box(
                    modifier = Modifier
                        .size(38.dp)
                        .clip(CircleShape)
                        .background(Color.Black.copy(alpha = 0.55f))
                        .clickable(onClick = onBackClick),
                    contentAlignment = Alignment.Center
                ) {
                    Icon(
                        imageVector = Icons.AutoMirrored.Filled.ArrowBack,
                        contentDescription = "Retour",
                        tint = Color.White
                    )
                }

                Text(
                    text = if (streaming) "WEBCAM PC • LIVE" else "WEBCAM PC • ARRÊTÉE",
                    color = Color.White,
                    fontSize = 11.sp
                )
            }
        }

        Column(
            modifier = Modifier
                .fillMaxWidth()
                .background(DarkNavy)
                .padding(14.dp)
        ) {
            Button(
                onClick = { streaming = !streaming },
                enabled = connected,
                modifier = Modifier.fillMaxWidth()
            ) {
                Text(
                    if (streaming) "Arrêter le flux webcam"
                    else "Démarrer la webcam PC"
                )
            }
            Spacer(modifier = Modifier.height(6.dp))
            Text(
                text = frame?.let {
                    it.width.toString() + "×" + it.height + " • " +
                        (it.fps ?: 0) + " FPS • JPEG Q" + (it.quality ?: 0)
                } ?: "Aucun flux reçu",
                color = Color.White.copy(alpha = 0.65f),
                fontSize = 10.sp
            )
        }
    }
}
