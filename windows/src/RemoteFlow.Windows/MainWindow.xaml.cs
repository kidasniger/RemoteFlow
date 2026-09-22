using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using RemoteFlow.Windows.Core;

namespace RemoteFlow.Windows;

public partial class MainWindow : Window
{
    private readonly RemoteFlowCore _core = new();
    private readonly ObservableCollection<RemoteFlowFileInfo> _fileItems = new();
    private string? _activeFileTransferId;

    public MainWindow()
    {
        InitializeComponent();
        _core.StateChanged += Core_StateChanged;
        _core.Server.StatusChanged += Server_StatusChanged;
        _core.Server.ClipboardStatusChanged += Server_ClipboardStatusChanged;
        _core.Server.FileTransferStatusChanged += Server_FileTransferStatusChanged;
        FilesList.ItemsSource = _fileItems;
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
        FilesView.Visibility = Visibility.Collapsed;
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
        FilesView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        PageTitle.Text = "Tableau de bord";
        PageSubtitle.Text = "Centre de contrôle RemoteFlow Windows.";
        RefreshPairingUi();
    }

    private void Connection_Click(object sender, RoutedEventArgs e)
    {
        DashboardView.Visibility = Visibility.Collapsed;
        ConnectionView.Visibility = Visibility.Visible;
        FilesView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        PageTitle.Text = "Connexion & appairage";
        PageSubtitle.Text = "QR, PIN et identité de sécurité RemoteFlow.";
        RefreshPairingUi();
    }

    private void Files_Click(object sender, RoutedEventArgs e)
    {
        DashboardView.Visibility = Visibility.Collapsed;
        ConnectionView.Visibility = Visibility.Collapsed;
        FilesView.Visibility = Visibility.Visible;
        PlaceholderView.Visibility = Visibility.Collapsed;
        PageTitle.Text = "Fichiers";
        PageSubtitle.Text = "Gestion native du dossier RemoteFlow et transferts avec le client connecté.";
        FilesRootPath.Text = _core.Server.FilesRootPath;
        RefreshFiles();
    }

    private void Screens_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Multi-écrans", "Détection des moniteurs Windows et streaming distant.");

    private void Macros_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Macros", "Raccourcis et commandes RemoteFlow natifs côté Windows.");


    private void RefreshFiles_Click(object sender, RoutedEventArgs e) => RefreshFiles();

    private void RefreshFiles()
    {
        try
        {
            var files = _core.Server.ListLocalFiles();
            _fileItems.Clear();
            foreach (var file in files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
                _fileItems.Add(file);

            FilesRootPath.Text = _core.Server.FilesRootPath;
            FilesTransferStatus.Text = $"{_fileItems.Count:N0} fichier(s) disponible(s) dans RemoteFlow.";
        }
        catch (Exception ex)
        {
            FilesTransferStatus.Text = $"Impossible de lire le dossier : {ex.Message}";
        }
    }

    private void OpenFilesFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _core.Server.FilesRootPath,
                UseShellExecute = true
            });
            FilesTransferStatus.Text = "Dossier RemoteFlow ouvert dans l'Explorateur.";
        }
        catch (Exception ex)
        {
            FilesTransferStatus.Text = $"Ouverture impossible : {ex.Message}";
        }
    }

    private async void ImportFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Ajouter un fichier à RemoteFlow",
            CheckFileExists = true,
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            FilesTransferStatus.Text = "Copie des fichiers en cours…";
            foreach (var sourcePath in dialog.FileNames)
                await Task.Run(() => _core.Server.ImportLocalFile(sourcePath));

            RefreshFiles();
            FilesTransferStatus.Text = $"{dialog.FileNames.Length:N0} fichier(s) ajouté(s) dans RemoteFlow.";
        }
        catch (Exception ex)
        {
            FilesTransferStatus.Text = $"Ajout impossible : {ex.Message}";
        }
    }

    private void OpenSelectedFile_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is not RemoteFlowFileInfo file)
        {
            FilesTransferStatus.Text = "Sélectionnez d'abord un fichier.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _core.Server.GetLocalFilePath(file.RelativePath),
                UseShellExecute = true
            });
            FilesTransferStatus.Text = $"Ouverture : {file.Name}";
        }
        catch (Exception ex)
        {
            FilesTransferStatus.Text = $"Ouverture impossible : {ex.Message}";
        }
    }

    private async void SendSelectedFile_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is not RemoteFlowFileInfo file)
        {
            FilesTransferStatus.Text = "Sélectionnez d'abord un fichier.";
            return;
        }

        try
        {
            FilesTransferStatus.Text = $"Préparation de l'envoi de {file.Name}…";
            var result = await _core.Server.SendFileToConnectedClientAsync(file.RelativePath);
            if (!result.Ok)
            {
                FilesTransferStatus.Text = result.Error ?? "Aucun client Android compatible connecté.";
                return;
            }

            _activeFileTransferId = result.TransferId;
            FilesTransferProgress.Visibility = Visibility.Visible;
            FilesTransferProgress.Value = 0;
            FilesTransferStatus.Text = $"Envoi de {file.Name} vers le client connecté…";
        }
        catch (Exception ex)
        {
            FilesTransferStatus.Text = $"Envoi impossible : {ex.Message}";
        }
    }

    private async void CancelFileTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_activeFileTransferId))
        {
            FilesTransferStatus.Text = "Aucun transfert actif.";
            return;
        }

        var transferId = _activeFileTransferId;
        try
        {
            await _core.Server.CancelFileTransferAsync(transferId);
            FilesTransferStatus.Text = "Annulation du transfert demandée.";
        }
        catch (Exception ex)
        {
            FilesTransferStatus.Text = $"Annulation impossible : {ex.Message}";
        }
    }

    private void DeleteSelectedFile_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is not RemoteFlowFileInfo file)
        {
            FilesTransferStatus.Text = "Sélectionnez d'abord un fichier.";
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Supprimer « {file.Name} » de RemoteFlow ?",
            "Supprimer le fichier",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            _core.Server.DeleteLocalFile(file.RelativePath);
            RefreshFiles();
            FilesTransferStatus.Text = $"Fichier supprimé : {file.Name}";
        }
        catch (Exception ex)
        {
            FilesTransferStatus.Text = $"Suppression impossible : {ex.Message}";
        }
    }

    private void Server_FileTransferStatusChanged(object? sender, RemoteFlowFileTransferState state)
    {
        Dispatcher.Invoke(() =>
        {
            if (!string.Equals(state.TransferId, _activeFileTransferId, StringComparison.Ordinal))
            {
                if (state.State is "progress" or "completed" or "cancelled")
                    return;
                _activeFileTransferId = state.TransferId;
            }

            FilesTransferProgress.Visibility = Visibility.Visible;
            if (state.TotalBytes > 0)
                FilesTransferProgress.Value = Math.Clamp(state.Offset * 100d / state.TotalBytes, 0d, 100d);

            FilesTransferStatus.Text = state.State switch
            {
                "started" => $"Transfert démarré : {state.FileName ?? state.TransferId}",
                "resumed" => $"Transfert repris : {state.FileName ?? state.TransferId}",
                "progress" => $"Transfert : {FormatBytes(state.Offset)} / {FormatBytes(state.TotalBytes)}",
                "completed" => $"Transfert terminé : {state.FileName ?? state.TransferId}",
                "cancelled" => $"Transfert annulé : {state.FileName ?? state.TransferId}",
                _ => $"Transfert {state.State} : {state.FileName ?? state.TransferId}"
            };

            if (state.State is "completed" or "cancelled")
            {
                _activeFileTransferId = null;
                FilesTransferProgress.Visibility = Visibility.Collapsed;
                if (FilesView.Visibility == Visibility.Visible)
                    RefreshFiles();
            }
        });
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} o";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.0} Ko";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):0.0} Mo";
        return $"{bytes / (1024d * 1024 * 1024):0.0} Go";
    }

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
        System.Windows.Clipboard.SetText(_core.Pairing.CreateQrPayload(_core.PairingPort));
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
