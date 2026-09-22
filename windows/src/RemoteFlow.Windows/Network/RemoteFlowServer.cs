using System.IO;
using System.Net;
using System.Net.Sockets;
using RemoteFlow.Windows.Clipboard;
using RemoteFlow.Windows.Core;
using RemoteFlow.Windows.Security;
using RemoteFlow.Windows.Input;
using RemoteFlow.Windows.Screen;
using RemoteFlow.Windows.Files;
using RemoteFlow.Windows.Macros;
using RemoteFlow.Windows.Webcam;

namespace RemoteFlow.Windows.Network;

public sealed class RemoteFlowServer : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly object _clipboardSessionsGate = new();
    private readonly object _clientsGate = new();
    private readonly PairingManager _pairing;
    private readonly FileTransferManager _files;
    private readonly ClipboardSyncManager _clipboard;
    private readonly MacroManager _macros;
    private readonly WebcamManager _webcam;
    private bool _clipboardSyncEnabled = true;
    private int _webcamBroadcastBusy;
    private readonly List<ClipboardSession> _clipboardSessions = new();
    private readonly List<ConnectedClient> _connectedClients = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; } = RemoteFlowProtocol.DefaultPort;
    public int ActiveConnections { get; private set; }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? ClipboardStatusChanged;
    public event EventHandler<RemoteFlowFileTransferState>? FileTransferStatusChanged;
    public event EventHandler<RemoteFlowWhiteboardStroke>? WhiteboardStrokeReceived;
    public event EventHandler<RemoteFlowServerEvent>? MessageReceived;
    public event EventHandler<RemoteFlowWebcamFrame>? WebcamFrameReceived;
    public event EventHandler<string>? WebcamStatusChanged;

    public RemoteFlowServer(PairingManager pairing)
    {
        _pairing = pairing;
        _files = new FileTransferManager();
        _clipboard = new ClipboardSyncManager();
        _macros = new MacroManager();
        _webcam = new WebcamManager();
        _webcam.FrameCaptured += Webcam_FrameCaptured;
        _webcam.StatusChanged += Webcam_StatusChanged;
        _clipboard.Changed += Clipboard_Changed;
        _files.TransferStatusChanged += Files_TransferStatusChanged;
    }

    public string FilesRootPath => _files.RootPath;
    public bool IsWebcamRunning => _webcam.IsRunning;
    public bool ClipboardSyncEnabled => Volatile.Read(ref _clipboardSyncEnabled);

    public void SetClipboardSyncEnabled(bool enabled)
    {
        Volatile.Write(ref _clipboardSyncEnabled, enabled);
        ClipboardStatusChanged?.Invoke(
            this,
            enabled
                ? "Synchronisation du presse-papiers activée."
                : "Synchronisation du presse-papiers désactivée.");
    }

    public IReadOnlyList<WebcamDeviceInfo> ListWebcams() => WebcamManager.DetectDevices();

    public async Task<(bool Ok, string? Error, string? Summary)> StartWebcamAsync(
        int cameraIndex,
        int width,
        int height,
        int fps,
        int quality,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var summary = await _webcam.StartAsync(cameraIndex, width, height, fps, quality, cancellationToken);
            return (true, null, summary);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
        {
            return (false, ex.Message, null);
        }
    }

    public async Task<(bool Ok, string? Error, string? Summary)> StopWebcamAsync()
    {
        try
        {
            var summary = await _webcam.StopAsync();
            return (true, null, summary);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
        {
            return (false, ex.Message, null);
        }
    }

    public IReadOnlyList<RemoteFlowMacro> ListMacros() => _macros.List();

    public RemoteFlowMacro SaveMacro(
        string? id,
        string name,
        IEnumerable<RemoteFlowMacroStep> steps) =>
        _macros.Save(id, name, steps);

    public void DeleteMacro(string id) => _macros.Delete(id);

    public async Task<(bool Ok, string? Error, string? Summary)> RunMacroAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return (false, "Identifiant de macro manquant.", null);

        try
        {
            await _macros.ExecuteAsync(id, cancellationToken);
            var macro = _macros.List().FirstOrDefault(x => x.Id == id);
            var summary = macro is null
                ? "Macro terminée."
                : $"Macro « {macro.Name} » terminée.";
            StatusChanged?.Invoke(this, summary);
            return (true, null, summary);
        }
        catch (OperationCanceledException)
        {
            return (false, "Exécution de la macro annulée.", null);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is KeyNotFoundException)
        {
            return (false, ex.Message, null);
        }
    }


    public IReadOnlyList<RemoteFlowFileInfo> ListLocalFiles() => _files.ListFiles();

    public string GetLocalFilePath(string relativePath) => _files.GetLocalFilePath(relativePath);

    public void DeleteLocalFile(string relativePath) => _files.DeleteFile(relativePath);

    public void ImportLocalFile(string sourcePath) => _files.ImportFile(sourcePath);

    public IReadOnlyList<WindowsMonitorInfo> ListMonitors() =>
        WindowsMonitorManager.GetMonitors();

    public async Task<(bool Ok, string? Error, string? Summary)> StartScreenStreamingAsync(
        int screenIndex,
        int fps,
        int maxWidth,
        int quality,
        CancellationToken cancellationToken = default)
    {
        ConnectedClient? client;
        lock (_clientsGate)
            client = _connectedClients.FirstOrDefault(x => x.IsAuthorized());

        if (client is null)
            return (false, "Aucun client RemoteFlow autorisé n'est connecté.", null);

        try
        {
            var summary = await client.ScreenStreaming.StartAsync(
                fps,
                maxWidth,
                quality,
                screenIndex,
                cancellationToken);
            StatusChanged?.Invoke(this, summary);
            return (true, null, summary);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException ||
            ex is IOException ||
            ex is SocketException)
        {
            return (false, ex.Message, null);
        }
    }

    public async Task<(bool Ok, string? Error, string? Summary)> StopScreenStreamingAsync(
        CancellationToken cancellationToken = default)
    {
        ConnectedClient? client;
        lock (_clientsGate)
            client = _connectedClients.FirstOrDefault(x => x.IsAuthorized());

        if (client is null)
            return (false, "Aucun client RemoteFlow autorisé n'est connecté.", null);

        try
        {
            var summary = await client.ScreenStreaming.StopAsync();
            StatusChanged?.Invoke(this, summary);
            return (true, null, summary);
        }
        catch (Exception ex) when (
            ex is IOException ||
            ex is SocketException)
        {
            return (false, ex.Message, null);
        }
    }

    public async Task<(bool Ok, string? Error, int SentCount)> BroadcastWhiteboardStrokeAsync(
        RemoteFlowWhiteboardStroke stroke,
        CancellationToken cancellationToken = default)
    {
        if (stroke.Points.Count is < 1 or > 5000)
            return (false, "Un nombre de points invalide.", 0);

        ConnectedClient[] clients;
        lock (_clientsGate)
            clients = _connectedClients.Where(x => x.IsAuthorized()).ToArray();

        var sentCount = 0;
        foreach (var client in clients)
        {
            try
            {
                await client.Session.SendAsync(stroke, cancellationToken);
                sentCount++;
            }
            catch
            {
            }
        }

        return (true, null, sentCount);
    }

    public async Task<(bool Ok, string? Error, string? TransferId)> SendFileToConnectedClientAsync(
        string relativePath,
        long offset = 0,
        CancellationToken cancellationToken = default)
    {
        ConnectedClient? client;
        lock (_clientsGate)
            client = _connectedClients.FirstOrDefault(x => x.IsAuthorized());

        if (client is null)
            return (false, "Aucun client RemoteFlow autorisé n'est connecté.", null);

        var transferId = $"win-{Guid.NewGuid():N}";
        try
        {
            await client.FileTransfers.StartDownloadAsync(
                client.Session,
                transferId,
                relativePath,
                offset,
                cancellationToken);

            return (true, null, transferId);
        }
        catch (Exception ex) when (
            ex is InvalidDataException ||
            ex is UnauthorizedAccessException ||
            ex is FileNotFoundException ||
            ex is IOException)
        {
            return (false, ex.Message, null);
        }
    }

    public async Task CancelFileTransferAsync(string transferId)
    {
        if (string.IsNullOrWhiteSpace(transferId))
            return;

        ConnectedClient[] clients;
        lock (_clientsGate)
            clients = _connectedClients.ToArray();

        foreach (var client in clients)
        {
            try { await client.FileTransfers.CancelDownloadAsync(transferId); } catch { }
        }
    }

    private void Files_TransferStatusChanged(object? sender, RemoteFlowFileTransferState state)
    {
        FileTransferStatusChanged?.Invoke(this, state);
    }

    public Task StartAsync(int port = RemoteFlowProtocol.DefaultPort)
    {
        lock (_gate)
        {
            if (IsRunning)
                return Task.CompletedTask;

            Port = port;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            IsRunning = true;
            _acceptTask = AcceptLoopAsync(_listener, _cts.Token);
        }

        StatusChanged?.Invoke(this, $"Écoute TCP sur 0.0.0.0:{Port}");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? acceptTask;
        lock (_gate)
        {
            if (!IsRunning)
                return;

            IsRunning = false;
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            acceptTask = _acceptTask;
            _listener = null;
            _acceptTask = null;
        }

        if (acceptTask is not null)
        {
            try { await acceptTask; } catch (OperationCanceledException) { }
        }

        StatusChanged?.Invoke(this, "Serveur RemoteFlow arrêté");
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        ActiveConnections++;
        StatusChanged?.Invoke(this, $"Client connecté : {client.Client.RemoteEndPoint}");

        await using var session = new JsonLineSession(client);
        var sessionPaired = !_pairing.PairingEnforced;
        string? clientDeviceId = null;
        var clipboardSession = new ClipboardSession(session, () => sessionPaired);
        AddClipboardSession(clipboardSession);
        await using var screenStreaming = new DesktopStreamController(session, cancellationToken);
        await using var fileTransfers = _files.CreateSession();
        var connectedClient = new ConnectedClient(
            session,
            fileTransfers,
            screenStreaming,
            () => sessionPaired);
        AddConnectedClient(connectedClient);

        try
        {
            await SendHelloAsync(session, cancellationToken);

            await session.RunAsync(async line =>
            {
                if (!RemoteFlowProtocol.TryParse(line, out var message) || message is null)
                {
                    await session.SendAsync(
                        new RemoteFlowAck(
                            Event: "ack",
                            Ok: false,
                            Error: "JSON invalide"),
                        cancellationToken);
                    return;
                }

                clientDeviceId ??= message.ClientDeviceId;

                if (string.Equals(message.Action, "hello", StringComparison.OrdinalIgnoreCase))
                {
                    await SendHelloAsync(session, cancellationToken);
                    return;
                }

                if (string.Equals(message.Action, "pair", StringComparison.OrdinalIgnoreCase))
                {
                    var paired = _pairing.TryPair(message.Pin, message.ClientDeviceId, message.ClientName);
                    if (paired)
                    {
                        sessionPaired = true;
                        clientDeviceId ??= message.ClientDeviceId;
                    }

                    await session.SendAsync(
                        new RemoteFlowAck(
                            Event: "pairing",
                            Ok: paired,
                            Action: "pair",
                            Error: paired ? null : "PIN incorrect",
                            Paired: paired,
                            DeviceId: _pairing.DeviceId),
                        cancellationToken);
                    StatusChanged?.Invoke(
                        this,
                        paired
                            ? $"Appairage accepté : {message.ClientName ?? "Android"}"
                            : $"Appairage refusé : {client.Client.RemoteEndPoint}");
                    ClipboardStatusChanged?.Invoke(
                        this,
                        paired ? "Appareil appairé • presse-papiers autorisé" : "Appairage refusé");
                    return;
                }

                if (_pairing.PairingEnforced && !sessionPaired)
                {
                    await session.SendAsync(
                        new RemoteFlowAck(
                            Event: "ack",
                            Ok: false,
                            Action: message.Action,
                            Error: "Appairage requis avant le contrôle distant",
                            Paired: false),
                        cancellationToken);
                    return;
                }

                if (string.Equals(message.Action, "clipboard", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ClipboardSyncEnabled)
                    {
                        await session.SendAsync(
                            new RemoteFlowAck(
                                Event: "ack",
                                Ok: false,
                                Action: "clipboard",
                                Error: "Synchronisation du presse-papiers désactivée dans les paramètres Windows.",
                                Paired: sessionPaired),
                            cancellationToken);
                        return;
                    }

                    var clipboardText = message.Text;
                    if (clipboardText is null)
                    {
                        await session.SendAsync(
                            new RemoteFlowAck(
                                Event: "ack",
                                Ok: false,
                                Action: "clipboard",
                                Error: "Texte du presse-papiers manquant",
                                Paired: sessionPaired),
                            cancellationToken);
                        return;
                    }

                    if (clipboardText.Length > ClipboardSyncManager.MaxTextLength)
                    {
                        await session.SendAsync(
                            new RemoteFlowAck(
                                Event: "ack",
                                Ok: false,
                                Action: "clipboard",
                                Error: $"Presse-papiers trop volumineux (maximum {ClipboardSyncManager.MaxTextLength:N0} caractères)",
                                Paired: sessionPaired),
                            cancellationToken);
                        return;
                    }

                    if (!_clipboard.TrySetText(clipboardText))
                    {
                        await session.SendAsync(
                            new RemoteFlowAck(
                                Event: "ack",
                                Ok: false,
                                Action: "clipboard",
                                Error: "Windows n'a pas pu modifier le presse-papiers",
                                Paired: sessionPaired),
                            cancellationToken);
                        return;
                    }

                    var clipboardSummary = $"Presse-papiers Android → Windows ({clipboardText.Length:N0} caractères)";
                    MessageReceived?.Invoke(
                        this,
                        new RemoteFlowServerEvent(
                            "clipboard",
                            null,
                            clipboardSummary,
                            DateTimeOffset.UtcNow));
                    ClipboardStatusChanged?.Invoke(this, clipboardSummary);

                    await session.SendAsync(
                        new RemoteFlowAck(
                            Event: "ack",
                            Ok: true,
                            Action: "clipboard",
                            Paired: sessionPaired),
                        cancellationToken);
                    return;
                }

                if (string.Equals(message.Action, "macro", StringComparison.OrdinalIgnoreCase))
                {
                    var macroType = message.Type?.Trim().ToUpperInvariant();

                    if (macroType == "LIST")
                    {
                        var macroList = _macros.List()
                            .Select(x => new RemoteFlowMacroInfo(
                                x.Id,
                                x.Name,
                                x.StepCount,
                                x.UpdatedAtUtc))
                            .ToArray();

                        await session.SendAsync(
                            new RemoteFlowMacroList(
                                Event: "macro_list",
                                Macros: macroList),
                            cancellationToken);
                        return;
                    }

                    if (macroType is "RUN" or null)
                    {
                        var macroId = message.Id ?? message.Cmd;
                        if (string.IsNullOrWhiteSpace(macroId))
                        {
                            await session.SendAsync(
                                new RemoteFlowAck(
                                    Event: "ack",
                                    Ok: false,
                                    Action: "macro",
                                    Error: "Identifiant de macro manquant.",
                                    Paired: sessionPaired),
                                cancellationToken);
                            return;
                        }

                        var runResult = await RunMacroAsync(macroId, cancellationToken);
                        if (!runResult.Ok)
                        {
                            await session.SendAsync(
                                new RemoteFlowAck(
                                    Event: "ack",
                                    Ok: false,
                                    Action: "macro",
                                    Error: runResult.Error,
                                    Paired: sessionPaired),
                                cancellationToken);
                            return;
                        }

                        MessageReceived?.Invoke(
                            this,
                            new RemoteFlowServerEvent(
                                "macro",
                                "RUN",
                                runResult.Summary,
                                DateTimeOffset.UtcNow));

                        await session.SendAsync(
                            new RemoteFlowAck(
                                Event: "ack",
                                Ok: true,
                                Action: "macro",
                                Paired: sessionPaired),
                            cancellationToken);
                        return;
                    }

                    await session.SendAsync(
                        new RemoteFlowAck(
                            Event: "ack",
                            Ok: false,
                            Action: "macro",
                            Error: $"Commande macro non prise en charge : {macroType ?? "(vide)"}",
                            Paired: sessionPaired),
                        cancellationToken);
                    return;
                }

                if (string.Equals(message.Action, "whiteboard", StringComparison.OrdinalIgnoreCase))
                {
                    var points = message.Points;
                    if (points is null || points.Count == 0)
                    {
                        var legacyCount = Math.Clamp(message.PointsCount ?? 0, 0, 5000);
                        if (legacyCount == 0)
                        {
                            await session.SendAsync(
                                new RemoteFlowAck(
                                    Event: "ack",
                                    Ok: false,
                                    Action: "whiteboard",
                                    Error: "Tracé sans points.",
                                    Paired: sessionPaired),
                                cancellationToken);
                            return;
                        }

                        var legacySummary = $"Tableau blanc reçu : {legacyCount:N0} points (coordonnées non fournies par ce client).";
                        MessageReceived?.Invoke(
                            this,
                            new RemoteFlowServerEvent(
                                "whiteboard",
                                "STROKE",
                                legacySummary,
                                DateTimeOffset.UtcNow));

                        await session.SendAsync(
                            new RemoteFlowAck(
                                Event: "ack",
                                Ok: true,
                                Action: "whiteboard",
                                Paired: sessionPaired),
                            cancellationToken);
                        return;
                    }

                    if (points.Count > 5000)
                    {
                        await session.SendAsync(
                            new RemoteFlowAck(
                                Event: "ack",
                                Ok: false,
                                Action: "whiteboard",
                                Error: "Tracé trop volumineux (maximum 5000 points).",
                                Paired: sessionPaired),
                            cancellationToken);
                        return;
                    }

                    if (points.Any(point => point.X is < 0f or > 1f || point.Y is < 0f or > 1f))
                    {
                        await session.SendAsync(
                            new RemoteFlowAck(
                                Event: "ack",
                                Ok: false,
                                Action: "whiteboard",
                                Error: "Les coordonnées du tracé doivent être normalisées entre 0 et 1.",
                                Paired: sessionPaired),
                            cancellationToken);
                        return;
                    }

                    var color = message.Color?.Trim() ?? "#2563EB";
                    if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$"))
                    {
                        await session.SendAsync(
                            new RemoteFlowAck(
                                Event: "ack",
                                Ok: false,
                                Action: "whiteboard",
                                Error: "Couleur de tracé invalide.",
                                Paired: sessionPaired),
                            cancellationToken);
                        return;
                    }

                    var width = Math.Clamp(message.Width ?? 6f, 1f, 100f);
                    var remoteStroke = new RemoteFlowWhiteboardStroke(
                        Event: "whiteboard_stroke",
                        Points: points.Take(5000).ToArray(),
                        Color: color,
                        Width: width,
                        Source: message.ClientName ?? "RemoteFlow",
                        Timestamp: message.Timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                    WhiteboardStrokeReceived?.Invoke(this, remoteStroke);
                    MessageReceived?.Invoke(
                        this,
                        new RemoteFlowServerEvent(
                            "whiteboard",
                            "STROKE",
                            $"Tableau blanc : {remoteStroke.Points.Count:N0} points reçus",
                            DateTimeOffset.UtcNow));

                    await BroadcastWhiteboardStrokeAsync(remoteStroke, cancellationToken);

                    await session.SendAsync(
                        new RemoteFlowAck(
                            Event: "ack",
                            Ok: true,
                            Action: "whiteboard",
                            Paired: sessionPaired),
                        cancellationToken);
                    return;
                }

                if (string.Equals(message.Action, "webcam", StringComparison.OrdinalIgnoreCase))
                {
                    var webcamType = message.Type?.Trim().ToUpperInvariant();

                    if (webcamType == "STOP")
                    {
                        var stopResult = await StopWebcamAsync();
                        await session.SendAsync(
                            new RemoteFlowWebcamState(
                                Event: "webcam_stream",
                                State: stopResult.Ok ? "stopped" : "error",
                                CameraIndex: -1,
                                CameraName: "",
                                Width: 0,
                                Height: 0,
                                Fps: 0,
                                Quality: 0),
                            cancellationToken);

                        await session.SendAsync(
                            new RemoteFlowAck(
                                Event: "ack",
                                Ok: stopResult.Ok,
                                Action: "webcam",
                                Error: stopResult.Error,
                                Paired: sessionPaired),
                            cancellationToken);
                        return;
                    }

                    if (webcamType == "START")
                    {
                        var startResult = await StartWebcamAsync(
                            message.CameraIndex ?? 0,
                            message.FrameWidth ?? 1280,
                            message.FrameHeight ?? 720,
                            message.Fps ?? 15,
                            message.Quality ?? 70,
                            cancellationToken);

                        if (!startResult.Ok)
                        {
                            await session.SendAsync(
                                new RemoteFlowAck(
                                    Event: "ack",
                                    Ok: false,
                                    Action: "webcam",
                                    Error: startResult.Error,
                                    Paired: sessionPaired),
                                cancellationToken);
                            return;
                        }

                        var device = WebcamManager.DetectDevices()
                            .FirstOrDefault(x => x.Index == (message.CameraIndex ?? 0));

                        await session.SendAsync(
                            new RemoteFlowWebcamState(
                                Event: "webcam_stream",
                                State: "started",
                                CameraIndex: message.CameraIndex ?? 0,
                                CameraName: device?.Name ?? $"Webcam {(message.CameraIndex ?? 0) + 1}",
                                Width: message.FrameWidth ?? 1280,
                                Height: message.FrameHeight ?? 720,
                                Fps: message.Fps ?? 15,
                                Quality: message.Quality ?? 70),
                            cancellationToken);

                        await session.SendAsync(
                            new RemoteFlowAck(
                                Event: "ack",
                                Ok: true,
                                Action: "webcam",
                                Paired: sessionPaired),
                            cancellationToken);
                        return;
                    }

                    await session.SendAsync(
                        new RemoteFlowAck(
                            Event: "ack",
                            Ok: false,
                            Action: "webcam",
                            Error: $"Commande webcam non prise en charge : {webcamType ?? "(vide)"}",
                            Paired: sessionPaired),
                        cancellationToken);
                    return;
                }

                if (string.Equals(message.Action, "files", StringComparison.OrdinalIgnoreCase))
                {
                    var fileResult = await HandleFileCommandAsync(
                        session,
                        fileTransfers,
                        message,
                        cancellationToken);

                    if (!fileResult.ok)
                    {
                        await session.SendAsync(
                            new RemoteFlowAck(
                                Event: "ack",
                                Ok: false,
                                Action: "files",
                                Error: fileResult.error,
                                Paired: sessionPaired,
                                TransferId: message.TransferId,
                                Offset: fileResult.offset,
                                TotalBytes: fileResult.totalBytes),
                            cancellationToken);
                    }

                    return;
                }

                if (string.Equals(message.Action, "screen", StringComparison.OrdinalIgnoreCase))
                {
                    var screenType = message.Type?.Trim().ToUpperInvariant();
                    if (screenType == "START")
                    {
                        var screenSummaryStart = await screenStreaming.StartAsync(
                            message.Fps ?? 8,
                            message.MaxWidth ?? 1280,
                            message.Quality ?? 60,
                            message.ScreenIndex ?? -1,
                            cancellationToken);

                        MessageReceived?.Invoke(
                            this,
                            new RemoteFlowServerEvent(
                                "screen",
                                "START",
                                screenSummaryStart,
                                DateTimeOffset.UtcNow));

                        await session.SendAsync(
                            new RemoteFlowAck(
                                Event: "ack",
                                Ok: true,
                                Action: "screen",
                                Paired: sessionPaired),
                            cancellationToken);
                        return;
                    }

                    if (screenType == "STOP")
                    {
                        var screenSummaryStop = await screenStreaming.StopAsync();

                        MessageReceived?.Invoke(
                            this,
                            new RemoteFlowServerEvent(
                                "screen",
                                "STOP",
                                screenSummaryStop,
                                DateTimeOffset.UtcNow));

                        await session.SendAsync(
                            new RemoteFlowAck(
                                Event: "ack",
                                Ok: true,
                                Action: "screen",
                                Paired: sessionPaired),
                            cancellationToken);
                        return;
                    }

                    await session.SendAsync(
                        new RemoteFlowAck(
                            Event: "ack",
                            Ok: false,
                            Action: "screen",
                            Error: $"Commande écran non prise en charge : {screenType ?? "(vide)"}",
                            Paired: sessionPaired),
                        cancellationToken);
                    return;
                }

                var execution = ExecuteAction(message);
                if (!execution.ok)
                {
                    await session.SendAsync(
                        new RemoteFlowAck(
                            Event: "ack",
                            Ok: false,
                            Action: message.Action,
                            Error: execution.error,
                            Paired: sessionPaired),
                        cancellationToken);
                    return;
                }

                var summary = execution.summary ?? BuildSummary(message);
                MessageReceived?.Invoke(
                    this,
                    new RemoteFlowServerEvent(
                        message.Action ?? "unknown",
                        message.Type,
                        summary,
                        DateTimeOffset.UtcNow));

                await session.SendAsync(
                    new RemoteFlowAck(Event: "ack", Ok: true, Action: message.Action, Paired: sessionPaired),
                    cancellationToken);
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        finally
        {
            RemoveClipboardSession(clipboardSession);
            RemoveConnectedClient(connectedClient);
            try { await screenStreaming.StopAsync(); } catch { }
            ActiveConnections = Math.Max(0, ActiveConnections - 1);
            StatusChanged?.Invoke(this, $"Client déconnecté : {client.Client.RemoteEndPoint}");
        }
    }


    private void AddConnectedClient(ConnectedClient client)
    {
        lock (_clientsGate)
            _connectedClients.Add(client);
    }

    private void RemoveConnectedClient(ConnectedClient client)
    {
        lock (_clientsGate)
            _connectedClients.Remove(client);
    }


    private void Webcam_StatusChanged(object? sender, string status)
    {
        WebcamStatusChanged?.Invoke(this, status);
        MessageReceived?.Invoke(
            this,
            new RemoteFlowServerEvent(
                "webcam",
                _webcam.IsRunning ? "RUNNING" : "STOPPED",
                status,
                DateTimeOffset.UtcNow));
    }

    private void Webcam_FrameCaptured(object? sender, RemoteFlowWebcamFrame frame)
    {
        WebcamFrameReceived?.Invoke(this, frame);
        _ = BroadcastWebcamFrameAsync(frame);
    }

    private async Task BroadcastWebcamFrameAsync(RemoteFlowWebcamFrame frame)
    {
        if (Interlocked.Exchange(ref _webcamBroadcastBusy, 1) != 0)
            return;

        try
        {
            ConnectedClient[] clients;
            lock (_clientsGate)
                clients = _connectedClients.Where(x => x.IsAuthorized()).ToArray();

            foreach (var client in clients)
            {
                try { await client.Session.SendAsync(frame); } catch { }
            }
        }
        finally
        {
            Volatile.Write(ref _webcamBroadcastBusy, 0);
        }
    }

    private void Clipboard_Changed(object? sender, ClipboardChangedEventArgs e)
    {
        if (!ClipboardSyncEnabled)
            return;

        var summary = $"Presse-papiers Windows modifié ({e.Text.Length:N0} caractères)";
        ClipboardStatusChanged?.Invoke(this, summary);
        MessageReceived?.Invoke(
            this,
            new RemoteFlowServerEvent(
                "clipboard",
                null,
                summary,
                DateTimeOffset.UtcNow));

        _ = BroadcastClipboardAsync(e.Text);
    }

    private void AddClipboardSession(ClipboardSession session)
    {
        lock (_clipboardSessionsGate)
            _clipboardSessions.Add(session);
    }

    private void RemoveClipboardSession(ClipboardSession session)
    {
        lock (_clipboardSessionsGate)
            _clipboardSessions.Remove(session);
    }

    private async Task BroadcastClipboardAsync(string text)
    {
        ClipboardSession[] sessions;
        lock (_clipboardSessionsGate)
            sessions = _clipboardSessions.ToArray();

        var payload = new RemoteFlowClipboardUpdate(
            Event: "clipboard",
            Text: text,
            Source: "windows",
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        foreach (var entry in sessions)
        {
            if (!entry.IsAuthorized())
                continue;

            try
            {
                await entry.Session.SendAsync(payload);
            }
            catch
            {
                RemoveClipboardSession(entry);
            }
        }
    }

    private async Task SendHelloAsync(JsonLineSession session, CancellationToken cancellationToken)
    {
        var security = _pairing.CreateSignedHello(Port);
        await session.SendAsync(
            new RemoteFlowHello(
                Port: Port,
                DeviceId: security.DeviceId,
                Fingerprint: security.Fingerprint,
                PublicKey: security.PublicKey,
                Nonce: security.Nonce,
                Signature: security.Signature,
                PairingRequired: security.PairingRequired,
                PinLength: security.PinLength,
                Security: security.Security),
            cancellationToken);
    }

    private async Task<(bool ok, string? error, long? offset, long? totalBytes)> HandleFileCommandAsync(
        JsonLineSession session,
        FileTransferManager.FileTransferSession transfers,
        RemoteFlowMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            var type = message.Type?.Trim().ToUpperInvariant();

            switch (type)
            {
                case "LIST":
                {
                    var list = _files.ListFiles();
                    await session.SendAsync(
                        new RemoteFlowFileList(
                            Event: "file_list",
                            Files: list,
                            Root: "Downloads/RemoteFlow",
                            Truncated: list.Count >= FileTransferManager.MaxListedFiles),
                        cancellationToken);
                    return (true, null, 0, 0);
                }

                case "UPLOAD_START":
                {
                    var state = await transfers.StartUploadAsync(
                        message.TransferId ?? string.Empty,
                        message.FileName ?? message.Path ?? string.Empty,
                        message.Size ?? -1,
                        message.Offset ?? 0,
                        cancellationToken);

                    await session.SendAsync(state, cancellationToken);
                    return (true, null, state.Offset, state.TotalBytes);
                }

                case "UPLOAD_CHUNK":
                {
                    var state = await transfers.WriteChunkAsync(
                        message.TransferId ?? string.Empty,
                        message.Offset ?? -1,
                        message.Data ?? string.Empty,
                        cancellationToken);

                    await session.SendAsync(state, cancellationToken);
                    return (true, null, state.Offset, state.TotalBytes);
                }

                case "UPLOAD_END":
                {
                    var state = await transfers.FinishUploadAsync(
                        message.TransferId ?? string.Empty,
                        cancellationToken);

                    await session.SendAsync(state, cancellationToken);
                    return (true, null, state.Offset, state.TotalBytes);
                }

                case "UPLOAD_CANCEL":
                    await transfers.CancelUploadAsync(message.TransferId ?? string.Empty);
                    await session.SendAsync(
                        new RemoteFlowFileTransferState(
                            Event: "file_transfer",
                            TransferId: message.TransferId ?? string.Empty,
                            State: "cancelled",
                            Offset: message.Offset ?? 0,
                            TotalBytes: message.TotalBytes ?? 0,
                            FileName: message.FileName),
                        cancellationToken);
                    return (true, null, message.Offset ?? 0, message.TotalBytes ?? 0);

                case "DOWNLOAD_START":
                {
                    var state = await transfers.StartDownloadAsync(
                        session,
                        message.TransferId ?? string.Empty,
                        message.FileName ?? message.Path ?? string.Empty,
                        message.Offset ?? 0,
                        cancellationToken);

                    return (true, null, state.Offset, state.TotalBytes);
                }

                case "DOWNLOAD_CANCEL":
                    await transfers.CancelDownloadAsync(message.TransferId ?? string.Empty);
                    await session.SendAsync(
                        new RemoteFlowFileTransferState(
                            Event: "file_transfer",
                            TransferId: message.TransferId ?? string.Empty,
                            State: "cancelled",
                            Offset: message.Offset ?? 0,
                            TotalBytes: message.TotalBytes ?? 0,
                            FileName: message.FileName),
                        cancellationToken);
                    return (true, null, message.Offset ?? 0, message.TotalBytes ?? 0);

                default:
                    return (false, $"Commande fichiers non prise en charge : {type ?? "(vide)"}", null, null);
            }
        }
        catch (Exception ex) when (
            ex is InvalidDataException ||
            ex is UnauthorizedAccessException ||
            ex is FileNotFoundException ||
            ex is IOException ||
            ex is InvalidOperationException)
        {
            return (false, ex.Message, message.Offset, message.TotalBytes);
        }
    }

    private static (bool ok, string? summary, string? error) ExecuteAction(RemoteFlowMessage message)
    {
        return message.Action?.Trim().ToLowerInvariant() switch
        {
            "mouse" => ExecuteMouse(message),
            "keyboard" => ExecuteKeyboard(message),
            _ => (true, null, null)
        };
    }

    private static (bool ok, string? summary, string? error) ExecuteMouse(RemoteFlowMessage message)
    {
        var type = message.Type?.Trim().ToUpperInvariant();
        if (!WindowsMouseController.Execute(type, message.X, message.Y, message.Dx, message.Dy))
            return (false, null, $"Commande souris non prise en charge : {type ?? "(vide)"}");

        return (true, BuildSummary(message), null);
    }

    private static (bool ok, string? summary, string? error) ExecuteKeyboard(RemoteFlowMessage message)
    {
        if (!WindowsKeyboardController.PressLabel(message.Key, message.Special == true, message.Modifier == true))
            return (false, null, $"Touche clavier non reconnue : {message.Key ?? "(vide)"}");

        return (true, BuildSummary(message), null);
    }

    private static string BuildSummary(RemoteFlowMessage message)
    {
        return message.Action?.ToLowerInvariant() switch
        {
            "mouse" => $"Souris {message.Type ?? "UNKNOWN"} ({message.X:0.##},{message.Y:0.##})",
            "keyboard" => $"Clavier {message.Key ?? "(vide)"}",
            "macro" => $"Macro {message.Id ?? "(sans id)"} : {message.Cmd ?? "(sans commande)"}",
            "whiteboard" => $"Tableau blanc : {message.PointsCount ?? 0} points",
            "clipboard" => "Presse-papiers reçu",
            _ => $"Action {message.Action ?? "inconnue"}"
        };
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _clipboard.Dispose();
        try { await _webcam.DisposeAsync(); } catch { }
        _cts?.Dispose();
    }

    private sealed record ClipboardSession(
        JsonLineSession Session,
        Func<bool> IsAuthorized);

    private sealed record ConnectedClient(
        JsonLineSession Session,
        FileTransferManager.FileTransferSession FileTransfers,
        DesktopStreamController ScreenStreaming,
        Func<bool> IsAuthorized);
}
