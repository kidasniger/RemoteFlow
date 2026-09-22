using System.Windows;
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
        Closed += MainWindow_Closed;
        _ = StartRemoteFlowServerAsync();
    }

    private async Task StartRemoteFlowServerAsync()
    {
        try
        {
            await _core.StartServerAsync();
            ConnectionStatus.Text = $"Serveur TCP • port {_core.PairingPort}";
        }
        catch (Exception ex)
        {
            ConnectionStatus.Text = $"Serveur indisponible • {ex.Message}";
        }
    }

    private void Server_StatusChanged(object? sender, string status)
    {
        Dispatcher.Invoke(() => ConnectionStatus.Text = status);
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
        PlaceholderView.Visibility = Visibility.Visible;
        PageTitle.Text = title;
        PageSubtitle.Text = "RemoteFlow Windows natif";
        PlaceholderTitle.Text = title;
        PlaceholderText.Text = description;
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        DashboardView.Visibility = Visibility.Visible;
        PlaceholderView.Visibility = Visibility.Collapsed;
        PageTitle.Text = "Tableau de bord";
        PageSubtitle.Text = "Centre de contrôle RemoteFlow Windows.";
    }

    private void Connection_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Connexion", "Appairage QR/PIN et connexion sécurisée avec l’application Android.");

    private void Files_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Fichiers", "Transfert bidirectionnel PC ↔ Android avec progression et reprise.");

    private void Screens_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Multi-écrans", "Détection des moniteurs Windows et streaming distant.");

    private void Macros_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Macros", "Raccourcis et commandes RemoteFlow natifs côté Windows.");

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

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        try { await _core.DisposeAsync(); } catch { }
    }
}
