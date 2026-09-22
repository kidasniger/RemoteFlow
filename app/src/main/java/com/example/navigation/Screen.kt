package com.example.navigation

sealed class Screen(val route: String) {
    object Splash : Screen("splash")
    object OnboardingControl : Screen("onboarding_control")
    object OnboardingShare : Screen("onboarding_share")
    object QrConnection : Screen("qr_connection")
    object Dashboard : Screen("dashboard")
    object RemoteDesktop : Screen("remote_desktop")
    object FileSharing : Screen("file_sharing")
    object Whiteboard : Screen("whiteboard")
    object GyroMouse : Screen("gyro_mouse")
    object Macros : Screen("macros")
    object Webcam : Screen("webcam")
    object Settings : Screen("settings")
    object Keyboard : Screen("keyboard")
}
