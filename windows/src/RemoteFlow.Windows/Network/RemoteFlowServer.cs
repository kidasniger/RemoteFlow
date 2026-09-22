using System.IO;
using System.Net;
using System.Net.Sockets;
using RemoteFlow.Windows.Clipboard;
using RemoteFlow.Windows.Core;
using RemoteFlow.Windows.Security;
using RemoteFlow.Windows.Input;
using RemoteFlow.Windows.Screen;
using RemoteFlow.Windows.Files;

namespace RemoteFlow.Windows.Network;

public sealed class RemoteFlowServer : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly object _clipboardSessionsGate = new();
    private readonly PairingManager _pairing;
    private readonly FileTransferManager _files;
    private readonly ClipboardSyncManager _clipboard;
    private readonly List<ClipboardSession> _clipboardSessions = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; } = RemoteFlowProtocol.DefaultPort;
    public int ActiveConnections { get; private set; }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? ClipboardStatusChanged;
    public event EventHandler<RemoteFlowServerEvent>? MessageReceived;

    public RemoteFlowServer(PairingManager pairing)
    {
        _pairing = pairing;
        _files = new FileTransferManager();
        _clipboard = new ClipboardSyncManager();
        _clipboard.Changed += Clipboard_Changed;
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

                    var summary = $"Presse-papiers Android → Windows ({clipboardText.Length:N0} caractères)";
                    MessageReceived?.Invoke(
                        this,
                        new RemoteFlowServerEvent(
                            "clipboard",
                            null,
                            summary,
                            DateTimeOffset.UtcNow));
                    ClipboardStatusChanged?.Invoke(this, summary);

                    await session.SendAsync(
                        new RemoteFlowAck(
                            Event: "ack",
                            Ok: true,
                            Action: "clipboard",
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
            try { await screenStreaming.StopAsync(); } catch { }
            ActiveConnections = Math.Max(0, ActiveConnections - 1);
            StatusChanged?.Invoke(this, $"Client déconnecté : {client.Client.RemoteEndPoint}");
        }
    }

    private void Clipboard_Changed(object? sender, ClipboardChangedEventArgs e)
    {
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
        _cts?.Dispose();
    }

    private sealed record ClipboardSession(
        JsonLineSession Session,
        Func<bool> IsAuthorized);
}
