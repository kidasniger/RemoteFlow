using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using Microsoft.Win32;
using RemoteFlow.Windows.Core;
using RemoteFlow.Windows.Screen;
using RemoteFlow.Windows.Macros;

namespace RemoteFlow.Windows;

public partial class MainWindow : Window
{
    private readonly RemoteFlowCore _core = new();
    private readonly ObservableCollection<RemoteFlowFileInfo> _fileItems = new();
    private readonly ObservableCollection<WindowsMonitorInfo> _screenItems = new();
    private readonly ObservableCollection<RemoteFlowMacro> _macroItems = new();
    private readonly ObservableCollection<RemoteFlowMacroStep> _macroSteps = new();
    private string? _activeFileTransferId;
    private string? _editingMacroId;
    private CancellationTokenSource? _macroCts;

    public MainWindow()
    {
        InitializeComponent();
        _core.StateChanged += Core_StateChanged;
        _core.Server.StatusChanged += Server_StatusChanged;
        _core.Server.ClipboardStatusChanged += Server_ClipboardStatusChanged;
        _core.Server.FileTransferStatusChanged += Server_FileTransferStatusChanged;
        FilesList.ItemsSource = _fileItems;
        ScreensList.ItemsSource = _screenItems;
        MacrosList.ItemsSource = _macroItems;
        MacroStepsList.ItemsSource = _macroSteps;
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
        ScreensView.Visibility = Visibility.Collapsed;
        MacrosView.Visibility = Visibility.Collapsed;
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
        ScreensView.Visibility = Visibility.Collapsed;
        MacrosView.Visibility = Visibility.Collapsed;
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
        ScreensView.Visibility = Visibility.Collapsed;
        MacrosView.Visibility = Visibility.Collapsed;
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
        ScreensView.Visibility = Visibility.Collapsed;
        MacrosView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        PageTitle.Text = "Fichiers";
        PageSubtitle.Text = "Gestion native du dossier RemoteFlow et transferts avec le client connecté.";
        FilesRootPath.Text = _core.Server.FilesRootPath;
        RefreshFiles();
    }

    private void Screens_Click(object sender, RoutedEventArgs e)
    {
        DashboardView.Visibility = Visibility.Collapsed;
        ConnectionView.Visibility = Visibility.Collapsed;
        FilesView.Visibility = Visibility.Collapsed;
        ScreensView.Visibility = Visibility.Visible;
        MacrosView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        PageTitle.Text = "Multi-écrans";
        PageSubtitle.Text = "Moniteurs Windows, sélection d’écran et streaming distant natif.";
        RefreshScreens();
    }

    private void RefreshScreens_Click(object sender, RoutedEventArgs e) => RefreshScreens();

    private void RefreshScreens()
    {
        try
        {
            var selectedIndex = (ScreensList.SelectedItem as WindowsMonitorInfo)?.Index;
            var monitors = _core.Server.ListMonitors();

            _screenItems.Clear();
            foreach (var monitor in monitors)
                _screenItems.Add(monitor);

            if (selectedIndex.HasValue)
            {
                var selected = _screenItems.FirstOrDefault(x => x.Index == selectedIndex.Value);
                if (selected is not null)
                    ScreensList.SelectedItem = selected;
            }

            ScreensDetectedText.Text = $"{_screenItems.Count:N0} moniteur(s) Windows détecté(s).";
            if (_screenItems.Count == 0)
                ScreensSelectedText.Text = "Aucun moniteur détecté.";
            else if (!CaptureAllScreens.IsChecked.GetValueOrDefault() && ScreensList.SelectedItem is null)
                ScreensList.SelectedIndex = _screenItems[0].Index;
        }
        catch (Exception ex)
        {
            ScreensDetectedText.Text = $"Détection impossible : {ex.Message}";
            ScreensSelectedText.Text = "Impossible de charger les écrans.";
        }
    }

    private void ScreensList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScreensList.SelectedItem is WindowsMonitorInfo monitor)
            ScreensSelectedText.Text = $"Écran sélectionné : {monitor.DisplayIndex} — {monitor.Name} • {monitor.Resolution}";
        else
            ScreensSelectedText.Text = "Sélectionnez un écran à diffuser.";
    }

    private static int GetComboInt(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var value))
            return value;

        throw new InvalidOperationException("Réglage de streaming invalide.");
    }

    private async void StartScreenStreaming_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var useVirtualDesktop = CaptureAllScreens.IsChecked == true;
            var screenIndex = useVirtualDesktop
                ? -1
                : (ScreensList.SelectedItem as WindowsMonitorInfo)?.Index ?? -2;

            if (screenIndex == -2)
            {
                ScreensStreamStatus.Text = "Sélectionnez un écran avant de démarrer le streaming.";
                return;
            }

            var fps = GetComboInt(ScreensFpsCombo);
            var maxWidth = GetComboInt(ScreensWidthCombo);
            var quality = GetComboInt(ScreensQualityCombo);

            ScreensStreamStatus.Text = "Démarrage du streaming…";
            var result = await _core.Server.StartScreenStreamingAsync(screenIndex, fps, maxWidth, quality);
            ScreensStreamStatus.Text = result.Ok
                ? result.Summary ?? "Streaming d’écran démarré."
                : result.Error ?? "Impossible de démarrer le streaming.";
        }
        catch (Exception ex)
        {
            ScreensStreamStatus.Text = $"Démarrage impossible : {ex.Message}";
        }
    }

    private async void StopScreenStreaming_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ScreensStreamStatus.Text = "Arrêt du streaming…";
            var result = await _core.Server.StopScreenStreamingAsync();
            ScreensStreamStatus.Text = result.Ok
                ? result.Summary ?? "Streaming d’écran arrêté."
                : result.Error ?? "Impossible d’arrêter le streaming.";
        }
        catch (Exception ex)
        {
            ScreensStreamStatus.Text = $"Arrêt impossible : {ex.Message}";
        }
    }



    private void Macros_Click(object sender, RoutedEventArgs e)
    {
        DashboardView.Visibility = Visibility.Collapsed;
        ConnectionView.Visibility = Visibility.Collapsed;
        FilesView.Visibility = Visibility.Collapsed;
        ScreensView.Visibility = Visibility.Collapsed;
        MacrosView.Visibility = Visibility.Visible;
        PlaceholderView.Visibility = Visibility.Collapsed;
        PageTitle.Text = "Macros";
        PageSubtitle.Text = "Séquences d’actions natives enregistrées localement et exécutables sur Windows.";
        RefreshMacros();
    }

    private void RefreshMacros_Click(object sender, RoutedEventArgs e) => RefreshMacros();

    private void RefreshMacros(string? selectId = null)
    {
        try
        {
            var macros = _core.Server.ListMacros();
            _macroItems.Clear();
            foreach (var macro in macros)
                _macroItems.Add(macro);

            MacrosCountText.Text = $"{_macroItems.Count:N0} macro(s) enregistrée(s).";

            if (!string.IsNullOrWhiteSpace(selectId))
            {
                var selected = _macroItems.FirstOrDefault(x => x.Id == selectId);
                if (selected is not null)
                    MacrosList.SelectedItem = selected;
            }
            else if (_macroItems.Count > 0 && MacrosList.SelectedItem is null)
            {
                MacrosList.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            MacroRunStatus.Text = $"Impossible de charger les macros : {ex.Message}";
        }
    }

    private void NewMacro_Click(object sender, RoutedEventArgs e)
    {
        _editingMacroId = null;
        MacroNameTextBox.Text = "Nouvelle macro";
        _macroSteps.Clear();
        MacrosList.SelectedItem = null;
        MacroRunStatus.Text = "Nouvelle macro prête à être configurée.";
    }

    private void MacrosList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MacrosList.SelectedItem is not RemoteFlowMacro macro)
            return;

        _editingMacroId = macro.Id;
        MacroNameTextBox.Text = macro.Name;
        _macroSteps.Clear();
        foreach (var step in macro.Steps)
            _macroSteps.Add(step);

        MacroRunStatus.Text = $"Macro « {macro.Name} » sélectionnée • {_macroSteps.Count:N0} étape(s).";
    }

    private void AddMacroStep_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var type = (MacroStepTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim().ToUpperInvariant();
            var value = MacroStepValueTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(type))
                throw new InvalidOperationException("Choisissez un type d’étape.");

            if (!int.TryParse(MacroStepDelayTextBox.Text.Trim(), out var delayMs) || delayMs < 0 || delayMs > 60000)
                throw new InvalidOperationException("La pause doit être comprise entre 0 et 60000 ms.");

            RemoteFlowMacroStep step = type switch
            {
                "TOUCHE" or "RACCOURCI" => CreateKeyboardStep(value, delayMs),
                "TEXTE" => CreateTextStep(value, delayMs),
                "SOURIS" => CreateMouseStep(value, delayMs),
                "ATTENTE" => CreateDelayStep(value),
                _ => throw new InvalidOperationException("Type d’étape inconnu.")
            };

            _macroSteps.Add(step);
            MacroStepValueTextBox.Clear();
            MacroStepDelayTextBox.Text = "0";
            MacroRunStatus.Text = $"Étape ajoutée • {_macroSteps.Count:N0} étape(s).";
        }
        catch (Exception ex)
        {
            MacroRunStatus.Text = ex.Message;
        }
    }

    private static RemoteFlowMacroStep CreateKeyboardStep(string value, int delayMs)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Indiquez une touche ou un raccourci, par exemple CTRL+S.");
        return new RemoteFlowMacroStep("keyboard", "PRESS", value, null, null, null, null, null, delayMs);
    }

    private static RemoteFlowMacroStep CreateTextStep(string value, int delayMs)
    {
        if (string.IsNullOrEmpty(value))
            throw new InvalidOperationException("Indiquez le texte à saisir.");
        if (value.Length > 10000)
            throw new InvalidOperationException("Un bloc de texte ne peut pas dépasser 10000 caractères.");
        return new RemoteFlowMacroStep("text", "TEXT", null, value, null, null, null, null, delayMs);
    }

    private static RemoteFlowMacroStep CreateMouseStep(string value, int delayMs)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Indiquez une commande souris.");

        var upper = value.ToUpperInvariant();
        if (upper.StartsWith("MOVE:", StringComparison.Ordinal))
        {
            var pair = value[5..].Split(',', StringSplitOptions.TrimEntries);
            if (pair.Length != 2 ||
                !float.TryParse(pair[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(pair[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) ||
                x is < 0f or > 1f || y is < 0f or > 1f)
                throw new InvalidOperationException("MOVE doit utiliser X,Y entre 0 et 1, par exemple MOVE:0.5,0.5.");

            return new RemoteFlowMacroStep("mouse", "MOVE", null, null, x, y, null, null, delayMs);
        }

        if (upper.StartsWith("MOVE_RELATIVE:", StringComparison.Ordinal))
        {
            var pair = value[14..].Split(',', StringSplitOptions.TrimEntries);
            if (pair.Length != 2 ||
                !float.TryParse(pair[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dx) ||
                !float.TryParse(pair[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dy))
                throw new InvalidOperationException("MOVE_RELATIVE doit utiliser DX,DY, par exemple MOVE_RELATIVE:0.1,0.");

            return new RemoteFlowMacroStep("mouse", "MOVE_RELATIVE", null, null, null, null, dx, dy, delayMs);
        }

        var allowed = new[] { "LEFT_CLICK", "RIGHT_CLICK", "DOUBLE_CLICK", "SCROLL_UP", "SCROLL_DOWN" };
        if (!allowed.Contains(upper, StringComparer.Ordinal))
            throw new InvalidOperationException("Commande souris invalide. Exemples : LEFT_CLICK, DOUBLE_CLICK, SCROLL_UP.");

        return new RemoteFlowMacroStep("mouse", upper, null, null, null, null, null, null, delayMs);
    }

    private static RemoteFlowMacroStep CreateDelayStep(string value)
    {
        if (!int.TryParse(value, out var delayMs) || delayMs < 1 || delayMs > 60000)
            throw new InvalidOperationException("Une attente doit être comprise entre 1 et 60000 ms.");
        return new RemoteFlowMacroStep("delay", "WAIT", null, null, null, null, null, null, delayMs);
    }

    private void RemoveMacroStep_Click(object sender, RoutedEventArgs e)
    {
        if (MacroStepsList.SelectedItem is not RemoteFlowMacroStep step)
        {
            MacroRunStatus.Text = "Sélectionnez une étape à supprimer.";
            return;
        }

        _macroSteps.Remove(step);
        MacroRunStatus.Text = $"Étape supprimée • {_macroSteps.Count:N0} étape(s).";
    }

    private void MoveMacroStepUp_Click(object sender, RoutedEventArgs e) => MoveMacroStep(-1);
    private void MoveMacroStepDown_Click(object sender, RoutedEventArgs e) => MoveMacroStep(1);

    private void MoveMacroStep(int direction)
    {
        var index = MacroStepsList.SelectedIndex;
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _macroSteps.Count)
            return;

        _macroSteps.Move(index, target);
        MacroStepsList.SelectedIndex = target;
    }

    private RemoteFlowMacro SaveCurrentMacro()
    {
        var name = MacroNameTextBox.Text.Trim();
        if (name.Length is < 1 or > 80)
            throw new InvalidOperationException("Le nom de la macro doit contenir entre 1 et 80 caractères.");
        if (_macroSteps.Count == 0)
            throw new InvalidOperationException("Ajoutez au moins une étape avant d’enregistrer.");

        var saved = _core.Server.SaveMacro(_editingMacroId, name, _macroSteps.ToArray());
        _editingMacroId = saved.Id;
        return saved;
    }

    private void SaveMacro_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saved = SaveCurrentMacro();
            RefreshMacros(saved.Id);
            MacroRunStatus.Text = $"Macro « {saved.Name} » enregistrée.";
        }
        catch (Exception ex)
        {
            MacroRunStatus.Text = $"Enregistrement impossible : {ex.Message}";
        }
    }

    private void DeleteMacro_Click(object sender, RoutedEventArgs e)
    {
        if (MacrosList.SelectedItem is not RemoteFlowMacro macro)
        {
            MacroRunStatus.Text = "Sélectionnez d’abord une macro.";
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Supprimer la macro « {macro.Name} » ?",
            "Supprimer la macro",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            _core.Server.DeleteMacro(macro.Id);
            _editingMacroId = null;
            _macroSteps.Clear();
            MacroNameTextBox.Clear();
            RefreshMacros();
            MacroRunStatus.Text = $"Macro « {macro.Name} » supprimée.";
        }
        catch (Exception ex)
        {
            MacroRunStatus.Text = $"Suppression impossible : {ex.Message}";
        }
    }

    private async void RunMacro_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saved = SaveCurrentMacro();
            RefreshMacros(saved.Id);

            _macroCts?.Cancel();
            _macroCts?.Dispose();
            _macroCts = new CancellationTokenSource();

            MacroRunStatus.Text = $"Exécution de « {saved.Name} »…";
            var result = await _core.Server.RunMacroAsync(saved.Id, _macroCts.Token);
            MacroRunStatus.Text = result.Ok
                ? result.Summary ?? "Macro terminée."
                : result.Error ?? "La macro n’a pas pu être exécutée.";
        }
        catch (OperationCanceledException)
        {
            MacroRunStatus.Text = "Exécution de la macro annulée.";
        }
        catch (Exception ex)
        {
            MacroRunStatus.Text = $"Exécution impossible : {ex.Message}";
        }
    }

    private void StopMacro_Click(object sender, RoutedEventArgs e)
    {
        if (_macroCts is null)
        {
            MacroRunStatus.Text = "Aucune exécution de macro active.";
            return;
        }

        _macroCts.Cancel();
        MacroRunStatus.Text = "Arrêt de la macro demandé.";
    }




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
