using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteFlow.Windows.Settings;

public sealed class RemoteFlowSettingsManager
{
    private readonly string _storageDirectory;
    private readonly string _storagePath;
    private readonly object _gate = new();

    public RemoteFlowSettings Settings { get; private set; }

    public RemoteFlowSettingsManager()
    {
        _storageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RemoteFlow");
        _storagePath = Path.Combine(_storageDirectory, "settings.json");
        Directory.CreateDirectory(_storageDirectory);
        Settings = Load();
    }

    public void Save()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_storageDirectory);
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            var temp = _storagePath + ".tmp";
            File.WriteAllText(temp, json, Encoding.UTF8);
            File.Move(temp, _storagePath, true);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            Settings = RemoteFlowSettings.Default();
            Save();
        }
    }

    private RemoteFlowSettings Load()
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                var json = File.ReadAllText(_storagePath);
                var settings = JsonSerializer.Deserialize<RemoteFlowSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch
        {
        }

        var defaults = RemoteFlowSettings.Default();
        try
        {
            var json = JsonSerializer.Serialize(defaults, JsonOptions);
            File.WriteAllText(_storagePath, json, Encoding.UTF8);
        }
        catch
        {
        }

        return defaults;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class RemoteFlowSettings
{
    public const int DefaultTcpPort = 8443;

    public int TcpPort { get; set; } = DefaultTcpPort;
    public bool ClipboardSyncEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool LaunchMinimized { get; set; }

    public static RemoteFlowSettings Default() => new();

    public void Normalize()
    {
        TcpPort = Math.Clamp(TcpPort, 1024, 65535);
    }
}
