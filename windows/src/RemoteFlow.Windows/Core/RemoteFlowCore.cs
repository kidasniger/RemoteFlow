using RemoteFlow.Windows.Network;

namespace RemoteFlow.Windows.Core;

public sealed class RemoteFlowCore : IAsyncDisposable
{
    public string Version { get; } = "1.0.0";
    public int PairingPort => RemoteFlowProtocol.DefaultPort;
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public RemoteFlowServer Server { get; }
    public event EventHandler<ConnectionState>? StateChanged;

    public RemoteFlowCore()
    {
        Server = new RemoteFlowServer();
        Server.StatusChanged += (_, status) =>
        {
            if (status.StartsWith("Client connecté", StringComparison.Ordinal))
                SetState(ConnectionState.Connected);
            else if (status.StartsWith("Client déconnecté", StringComparison.Ordinal) && Server.ActiveConnections == 0)
                SetState(ConnectionState.Disconnected);
        };
    }

    public async Task StartServerAsync()
    {
        SetState(ConnectionState.Connecting);
        await Server.StartAsync(PairingPort);
    }

    public async Task StopServerAsync()
    {
        await Server.StopAsync();
        SetState(ConnectionState.Disconnected);
    }

    private void SetState(ConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public ValueTask DisposeAsync() => Server.DisposeAsync();
}

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected
}
