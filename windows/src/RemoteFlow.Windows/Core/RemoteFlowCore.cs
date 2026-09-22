namespace RemoteFlow.Windows.Core;
public sealed class RemoteFlowCore
{
    public string Version => "1.0.0";
    public string PairingPort => "8080";
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public event EventHandler<ConnectionState>? StateChanged;
    public void Connect() { State = ConnectionState.Connected; StateChanged?.Invoke(this, State); }
    public void Disconnect() { State = ConnectionState.Disconnected; StateChanged?.Invoke(this, State); }
}
public enum ConnectionState { Disconnected, Connecting, Connected }
