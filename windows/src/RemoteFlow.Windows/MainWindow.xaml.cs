using System.Windows;
using RemoteFlow.Windows.Core;
namespace RemoteFlow.Windows;
public partial class MainWindow : Window
{
    private readonly RemoteFlowCore _core = new();
    public MainWindow() { InitializeComponent(); _core.StateChanged += Core_StateChanged; }
    private void Core_StateChanged(object? sender, ConnectionState state) { Dispatcher.Invoke(() => ConnectionStatus.Text = state == ConnectionState.Connected ? "Connecté" : state == ConnectionState.Connecting ? "Connexion…" : "Prêt"); }
    private void ShowPage(string title, string description) { DashboardView.Visibility = Visibility.Collapsed; PlaceholderView.Visibility = Visibility.Visible; PageTitle.Text = title; PageSubtitle.Text = "RemoteFlow Windows natif"; PlaceholderTitle.Text = title; PlaceholderText.Text = description; }
    private void Dashboard_Click(object sender, RoutedEventArgs e) { DashboardView.Visibility = Visibility.Visible; PlaceholderView.Visibility = Visibility.Collapsed; PageTitle.Text = "Tableau de bord"; PageSubtitle.Text = "Centre de contrôle RemoteFlow Windows."; }
    private void Connection_Click(object sender, RoutedEventArgs e) => ShowPage("Connexion", "Appairage QR/PIN et connexion sécurisée avec l’application Android.");
    private void Files_Click(object sender, RoutedEventArgs e) => ShowPage("Fichiers", "Transfert bidirectionnel PC ↔ Android avec progression et reprise.");
    private void Screens_Click(object sender, RoutedEventArgs e) => ShowPage("Multi-écrans", "Détection des moniteurs Windows et streaming distant.");
    private void Macros_Click(object sender, RoutedEventArgs e) => ShowPage("Macros", "Raccourcis et commandes RemoteFlow natifs côté Windows.");
    private void Webcam_Click(object sender, RoutedEventArgs e) => ShowPage("Webcam", "Capture caméra réseau du téléphone et intégration webcam Windows.");
    private void Settings_Click(object sender, RoutedEventArgs e) => ShowPage("Paramètres & sécurité", "Paramètres système, chiffrement, réseau, UPnP et démarrage.");
    private void ConnectNow_Click(object sender, RoutedEventArgs e) => _core.Connect();
}
