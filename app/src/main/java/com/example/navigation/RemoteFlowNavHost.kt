package com.example.navigation

import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.navigation.NavHostController
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.example.clipboard.ClipboardSyncManager
import com.example.files.FileManager
import com.example.macros.MacroRepository
import com.example.network.RemotePcClient
import com.example.sensors.GyroscopeManager
import com.example.settings.SettingsManager
import com.example.ui.screens.DashboardScreen
import com.example.ui.screens.FileSharingScreen
import com.example.ui.screens.GyroMouseScreen
import com.example.ui.screens.KeyboardScreen
import com.example.ui.screens.MacrosScreen
import com.example.ui.screens.OnboardingControlScreen
import com.example.ui.screens.OnboardingShareScreen
import com.example.ui.screens.QrConnectionScreen
import com.example.ui.screens.RemoteDesktopScreen
import com.example.ui.screens.SettingsScreen
import com.example.ui.screens.SplashScreen
import com.example.ui.screens.WebcamScreen
import com.example.ui.screens.WhiteboardScreen

@Composable
fun RemoteFlowNavHost(
    modifier: Modifier = Modifier,
    navController: NavHostController = rememberNavController(),
    remoteClient: RemotePcClient,
    gyroManager: GyroscopeManager,
    clipboardManager: ClipboardSyncManager,
    fileManager: FileManager,
    macroRepository: MacroRepository,
    settingsManager: SettingsManager
) {
    NavHost(
        navController = navController,
        startDestination = Screen.Splash.route,
        modifier = modifier
    ) {
        composable(Screen.Splash.route) {
            SplashScreen(
                onNavigateNext = {
                    navController.navigate(Screen.OnboardingControl.route) {
                        popUpTo(Screen.Splash.route) { inclusive = true }
                    }
                }
            )
        }

        composable(Screen.OnboardingControl.route) {
            OnboardingControlScreen(
                onNext = { navController.navigate(Screen.OnboardingShare.route) },
                onSkip = {
                    navController.navigate(Screen.QrConnection.route) {
                        popUpTo(Screen.OnboardingControl.route) { inclusive = true }
                    }
                }
            )
        }

        composable(Screen.OnboardingShare.route) {
            OnboardingShareScreen(
                onStart = {
                    navController.navigate(Screen.QrConnection.route) {
                        popUpTo(Screen.OnboardingShare.route) { inclusive = true }
                    }
                }
            )
        }

        composable(Screen.QrConnection.route) {
            QrConnectionScreen(
                remoteClient = remoteClient,
                onConnected = {
                    navController.navigate(Screen.Dashboard.route) {
                        popUpTo(Screen.QrConnection.route) { inclusive = true }
                    }
                }
            )
        }

        composable(Screen.Dashboard.route) {
            DashboardScreen(
                remoteClient = remoteClient,
                onNavigateTo = { route ->
                    navController.navigate(route)
                }
            )
        }

        composable(Screen.RemoteDesktop.route) {
            RemoteDesktopScreen(
                remoteClient = remoteClient,
                onBackClick = { navController.popBackStack() },
                onOpenKeyboard = { navController.navigate(Screen.Keyboard.route) }
            )
        }

        composable(Screen.FileSharing.route) {
            FileSharingScreen(
                fileManager = fileManager,
                onBackClick = { navController.popBackStack() }
            )
        }

        composable(Screen.Whiteboard.route) {
            WhiteboardScreen(
                remoteClient = remoteClient,
                onBackClick = { navController.popBackStack() }
            )
        }

        composable(Screen.GyroMouse.route) {
            GyroMouseScreen(
                gyroManager = gyroManager,
                remoteClient = remoteClient,
                onBackClick = { navController.popBackStack() }
            )
        }

        composable(Screen.Macros.route) {
            MacrosScreen(
                macroRepository = macroRepository,
                onBackClick = { navController.popBackStack() }
            )
        }

        composable(Screen.Webcam.route) {
            WebcamScreen(
                onBackClick = { navController.popBackStack() }
            )
        }

        composable(Screen.Keyboard.route) {
            KeyboardScreen(
                remoteClient = remoteClient,
                onBackClick = { navController.popBackStack() }
            )
        }

        composable(Screen.Settings.route) {
            SettingsScreen(
                settingsManager = settingsManager,
                remoteClient = remoteClient,
                onBackClick = { navController.popBackStack() },
                onDisconnect = {
                    navController.navigate(Screen.QrConnection.route) {
                        popUpTo(Screen.Dashboard.route) { inclusive = true }
                    }
                }
            )
        }
    }
}
