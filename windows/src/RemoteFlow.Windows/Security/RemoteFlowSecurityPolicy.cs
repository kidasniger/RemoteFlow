namespace RemoteFlow.Windows.Security;

public static class RemoteFlowSecurityPolicy
{
    public const int MaxIncomingLineBytes = 4 * 1024 * 1024;
    public const int MaxConcurrentConnections = 8;
    public const int MaxPairingFailures = 5;
    public const int PairingFailureWindowSeconds = 60;
    public const int PairingBlockSeconds = 60;
    public const int MaxJsonDepth = 16;
    public const int MaxActionLength = 32;
    public const int MaxTypeLength = 32;
    public const int MaxKeyLength = 128;
    public const int MaxIdentifierLength = 128;
    public const int MaxClientDeviceIdLength = 256;
    public const int MaxClientNameLength = 256;
    public const int MaxFileNameLength = 1024;
    public const int MaxPathLength = 2048;
    public const int MaxColorLength = 32;
    public const int MaxPinLength = 32;
    public const int MaxTextLength = 1_000_000;
    public const int MaxDataCharacters = 256_000;
    public const int MaxWhiteboardPoints = 5_000;
    public const long MaxFileSizeBytes = 4L * 1024 * 1024 * 1024;
}
