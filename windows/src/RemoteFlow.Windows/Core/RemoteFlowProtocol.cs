using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteFlow.Windows.Core;

public static class RemoteFlowProtocol
{
    public const int CurrentVersion = 1;
    public const string DefaultHost = "0.0.0.0";
    public const int DefaultPort = 8443;
    public const string ProtocolName = "remoteflow-jsonl/1";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static bool TryParse(string line, out RemoteFlowMessage? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        try
        {
            message = JsonSerializer.Deserialize<RemoteFlowMessage>(line, JsonOptions);
            return message is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string Serialize(object message) =>
        JsonSerializer.Serialize(message, JsonOptions);
}

public sealed record RemoteFlowMessage
{
    public string? Action { get; init; }
    public string? Type { get; init; }
    public string? Key { get; init; }
    public bool? Special { get; init; }
    public bool? Modifier { get; init; }
    public float? X { get; init; }
    public float? Y { get; init; }
    public float? Dx { get; init; }
    public float? Dy { get; init; }
    public string? Id { get; init; }
    public string? Cmd { get; init; }
    public string? Color { get; init; }
    public float? Width { get; init; }
    public int? PointsCount { get; init; }
    public string? Text { get; init; }
    public long? Timestamp { get; init; }
    public int? Version { get; init; }
    public string? Pin { get; init; }
    public string? ClientDeviceId { get; init; }
    public string? ClientName { get; init; }
    public int? Quality { get; init; }
    public int? MaxWidth { get; init; }
    public int? Fps { get; init; }
}

public sealed record RemoteFlowServerEvent(
    string Action,
    string? Type,
    string? Summary,
    DateTimeOffset ReceivedAtUtc);

public sealed record RemoteFlowHello(
    string Event = "hello",
    int Version = RemoteFlowProtocol.CurrentVersion,
    string Product = "RemoteFlow",
    string Platform = "windows",
    int Port = RemoteFlowProtocol.DefaultPort,
    string Protocol = RemoteFlowProtocol.ProtocolName,
    string? DeviceId = null,
    string? Fingerprint = null,
    string? PublicKey = null,
    string? Nonce = null,
    string? Signature = null,
    bool PairingRequired = false,
    int PinLength = 6,
    string Security = "identity+p256");

public sealed record RemoteFlowAck(
    string Event,
    bool Ok = true,
    string? Action = null,
    string? Error = null,
    string Protocol = RemoteFlowProtocol.ProtocolName,
    bool? Paired = null,
    string? DeviceId = null);
