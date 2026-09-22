using System.IO;
using System.Net;
using System.Net.Sockets;
using RemoteFlow.Windows.Core;
using RemoteFlow.Windows.Security;
using RemoteFlow.Windows.Input;

namespace RemoteFlow.Windows.Network;

public sealed class RemoteFlowServer : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly PairingManager _pairing;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; } = RemoteFlowProtocol.DefaultPort;
    public int ActiveConnections { get; private set; }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<RemoteFlowServerEvent>? MessageReceived;

    public RemoteFlowServer(PairingManager pairing)
    {
        _pairing = pairing;
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
            ActiveConnections = Math.Max(0, ActiveConnections - 1);
            StatusChanged?.Invoke(this, $"Client déconnecté : {client.Client.RemoteEndPoint}");
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
        _cts?.Dispose();
    }
}
