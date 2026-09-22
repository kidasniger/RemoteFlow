using System.IO;
using System.Net;
using System.Net.Sockets;
using RemoteFlow.Windows.Core;

namespace RemoteFlow.Windows.Network;

public sealed class RemoteFlowServer : IAsyncDisposable
{
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; } = RemoteFlowProtocol.DefaultPort;
    public int ActiveConnections { get; private set; }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<RemoteFlowServerEvent>? MessageReceived;

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

        try
        {
            await session.SendAsync(new RemoteFlowHello(Port: Port), cancellationToken);

            await session.RunAsync(async line =>
            {
                if (!RemoteFlowProtocol.TryParse(line, out var message) || message is null)
                {
                    await session.SendAsync(new RemoteFlowAck(
                        Event: "ack",
                        Ok: false,
                        Error: "JSON invalide"), cancellationToken);
                    return;
                }

                if (string.Equals(message.Action, "hello", StringComparison.OrdinalIgnoreCase))
                {
                    await session.SendAsync(new RemoteFlowHello(Port: Port), cancellationToken);
                    return;
                }

                var summary = BuildSummary(message);
                MessageReceived?.Invoke(
                    this,
                    new RemoteFlowServerEvent(
                        message.Action ?? "unknown",
                        message.Type,
                        summary,
                        DateTimeOffset.UtcNow));

                await session.SendAsync(
                    new RemoteFlowAck(Event: "ack", Ok: true, Action: message.Action),
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
