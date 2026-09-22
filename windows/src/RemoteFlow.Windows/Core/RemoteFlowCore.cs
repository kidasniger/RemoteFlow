using RemoteFlow.Windows.Network;
using RemoteFlow.Windows.Security;
using RemoteFlow.Windows.Settings;

namespace RemoteFlow.Windows.Core;

public sealed class RemoteFlowCore : IAsyncDisposable
{
    public string Version { get; } = "1.0.0";
    public int PairingPort => Settings.Settings.TcpPort;
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public PairingManager Pairing { get; }
    public RemoteFlowServer Server { get; }
    public event EventHandler<ConnectionState>? StateChanged;

    public RemoteFlowSettingsManager Settings { get; }

    public RemoteFlowCore()
    {
        Settings = new RemoteFlowSettingsManager();
        Pairing = new PairingManager();
        Server = new RemoteFlowServer(Pairing);
        Server.SetClipboardSyncEnabled(Settings.Settings.ClipboardSyncEnabled);
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
