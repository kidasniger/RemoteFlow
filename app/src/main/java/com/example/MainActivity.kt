package com.example

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.lifecycleScope
import com.example.clipboard.ClipboardSyncManager
import com.example.domain.model.ConnectionState
import com.example.files.FileManager
import com.example.macros.MacroRepository
import com.example.navigation.RemoteFlowNavHost
import com.example.network.DefaultRemotePcClient
import com.example.sensors.GyroscopeManager
import com.example.settings.SettingsManager
import com.example.ui.theme.RemoteFlowTheme
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {

    private lateinit var remoteClient: DefaultRemotePcClient
    private lateinit var gyroManager: GyroscopeManager
    private lateinit var clipboardManager: ClipboardSyncManager
    private lateinit var fileManager: FileManager
    private lateinit var macroRepository: MacroRepository
    private lateinit var settingsManager: SettingsManager

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        settingsManager = SettingsManager(this)
        remoteClient = DefaultRemotePcClient(this)
        gyroManager = GyroscopeManager(this)
        clipboardManager = ClipboardSyncManager(this)
        fileManager = FileManager(this, remoteClient)
        macroRepository = MacroRepository(remoteClient)

        clipboardManager.setLocalClipboardSender { text ->
            if (settingsManager.settings.value.universalClipboard) {
                remoteClient.sendClipboard(text)
            }
        }

        remoteClient.setIncomingClipboardReceiver { text ->
            if (settingsManager.settings.value.universalClipboard) {
                clipboardManager.copyToLocal(text, fromRemote = true)
            }
        }

        lifecycleScope.launch {
            settingsManager.settings.collectLatest { userSettings ->
                clipboardManager.setSyncEnabled(userSettings.universalClipboard)
            }
        }

        lifecycleScope.launch {
            remoteClient.connectionState.collectLatest { state ->
                if (state is ConnectionState.Connected) {
                    fileManager.refreshPcFiles()
                }
            }
        }

        setContent {
            val userSettings by settingsManager.settings.collectAsState()

            RemoteFlowTheme(darkTheme = userSettings.darkTheme) {
                Surface(modifier = Modifier.fillMaxSize()) {
                    RemoteFlowNavHost(
                        remoteClient = remoteClient,
                        gyroManager = gyroManager,
                        clipboardManager = clipboardManager,
                        fileManager = fileManager,
                        macroRepository = macroRepository,
                        settingsManager = settingsManager
                    )
                }
            }
        }
    }

    override fun onDestroy() {
        remoteClient.disconnect()
        gyroManager.stop()
        clipboardManager.cleanup()
        super.onDestroy()
    }
}
