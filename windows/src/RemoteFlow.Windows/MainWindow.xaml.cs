using System.Windows;
using System.Windows.Media.Imaging;
using RemoteFlow.Windows.Core;

namespace RemoteFlow.Windows;

public partial class MainWindow : Window
{
    private readonly RemoteFlowCore _core = new();

    public MainWindow()
    {
        InitializeComponent();
        _core.StateChanged += Core_StateChanged;
        _core.Server.StatusChanged += Server_StatusChanged;
        _core.Server.ClipboardStatusChanged += Server_ClipboardStatusChanged;
        Closed += MainWindow_Closed;

        RefreshPairingUi();
        _ = StartRemoteFlowServerAsync();
    }

    private async Task StartRemoteFlowServerAsync()
    {
        try
        {
            await _core.StartServerAsync();
            RefreshPairingUi();
            ConnectionStatus.Text = $"Serveur TCP • port {_core.PairingPort}";
        }
        catch (Exception ex)
        {
            ConnectionStatus.Text = $"Serveur indisponible • {ex.Message}";
        }
    }

    private void RefreshPairingUi()
    {
        var payload = _core.Pairing.CreateQrPayload(_core.PairingPort);
        PairingQrImage.Source = _core.Pairing.CreateQrImage(_core.PairingPort);
        PairingAddress.Text = payload;
        PairingPin.Text = _core.Pairing.CurrentPin;
        PairingDeviceId.Text = _core.Pairing.DeviceId;
        PairingFingerprint.Text = _core.Pairing.Fingerprint;
        PairingSecurityState.Text = $"Identité persistante • {_core.Pairing.PairedDeviceCount} appareil(s) appairé(s)";
        PairingCompatibilityState.Text = _core.Pairing.PairingEnforced
            ? "Verrouillage actif : seuls les appareils appairés peuvent envoyer des commandes."
            : "Compatibilité actuelle : le client Android v1 n’envoie pas encore l’étape pair/PIN ; le verrouillage reste désactivé par défaut.";
        DashboardPairingAddress.Text = payload;
        DashboardPairingPin.Text = $"PIN : {_core.Pairing.CurrentPin}";
    }

    private void Server_StatusChanged(object? sender, string status)
    {
        Dispatcher.Invoke(() => ConnectionStatus.Text = status);
    }

    private void Server_ClipboardStatusChanged(object? sender, string status)
    {
        Dispatcher.Invoke(() =>
        {
            DashboardClipboardStatus.Text = status;
            PairingClipboardStatus.Text = status;
        });
    }

    private void Core_StateChanged(object? sender, ConnectionState state)
    {
        Dispatcher.Invoke(() =>
        {
            if (state == ConnectionState.Connected)
                ConnectionStatus.Text = "Android connecté";
            else if (state == ConnectionState.Disconnected && _core.Server.IsRunning)
                ConnectionStatus.Text = $"Serveur TCP • port {_core.PairingPort}";
        });
    }

    private void ShowPage(string title, string description)
    {
        DashboardView.Visibility = Visibility.Collapsed;
        ConnectionView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Visible;
        PageTitle.Text = title;
        PageSubtitle.Text = "RemoteFlow Windows natif";
        PlaceholderTitle.Text = title;
        PlaceholderText.Text = description;
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        DashboardView.Visibility = Visibility.Visible;
        ConnectionView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        PageTitle.Text = "Tableau de bord";
        PageSubtitle.Text = "Centre de contrôle RemoteFlow Windows.";
        RefreshPairingUi();
    }

    private void Connection_Click(object sender, RoutedEventArgs e)
    {
        DashboardView.Visibility = Visibility.Collapsed;
        ConnectionView.Visibility = Visibility.Visible;
        PlaceholderView.Visibility = Visibility.Collapsed;
        PageTitle.Text = "Connexion & appairage";
        PageSubtitle.Text = "QR, PIN et identité de sécurité RemoteFlow.";
        RefreshPairingUi();
    }

    private void Files_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Fichiers", "Transfert bidirectionnel PC ↔ Android avec progression et reprise.");

    private void Screens_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Multi-écrans", "Détection des moniteurs Windows et streaming distant.");

    private void Macros_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Macros", "Raccourcis et commandes RemoteFlow natifs côté Windows.");

    private void Clipboard_Click(object sender, RoutedEventArgs e) =>
        ShowPage(
            "Presse-papiers universel",
            "Synchronisation locale du texte entre Windows et les clients RemoteFlow compatibles. Aucun serveur cloud ni Internet n'est utilisé.");

    private void Webcam_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Webcam", "Capture caméra réseau du téléphone et intégration webcam Windows.");

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Paramètres & sécurité", "Paramètres système, chiffrement, réseau, UPnP et démarrage.");

    private void ConnectNow_Click(object sender, RoutedEventArgs e)
    {
        ConnectionStatus.Text = _core.Server.IsRunning
            ? $"Serveur TCP prêt • port {_core.PairingPort}"
            : "Serveur TCP arrêté";
    }

    private void CopyPairingAddress_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_core.Pairing.CreateQrPayload(_core.PairingPort));
        ConnectionStatus.Text = "Adresse d'appairage copiée";
    }

    private void RegeneratePin_Click(object sender, RoutedEventArgs e)
    {
        _core.Pairing.RegeneratePin();
        RefreshPairingUi();
        ConnectionStatus.Text = "Nouveau PIN généré et protégé";
    }

    private void TogglePairingEnforcement_Click(object sender, RoutedEventArgs e)
    {
        var next = !_core.Pairing.PairingEnforced;
        _core.Pairing.SetPairingEnforced(next);
        RefreshPairingUi();
        ConnectionStatus.Text = next
            ? "Verrouillage par appairage activé"
            : "Verrouillage par appairage désactivé";
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        try { await _core.DisposeAsync(); } catch { }
    }
}
