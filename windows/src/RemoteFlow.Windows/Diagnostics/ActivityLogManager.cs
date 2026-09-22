using System.IO;
using System.Text.Json;

namespace RemoteFlow.Windows.Diagnostics;

public sealed record RemoteFlowActivityEntry(
    DateTimeOffset TimestampUtc,
    string Category,
    string Source,
    string Level,
    string Summary)
{
    public string LocalTimestamp => TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}

public sealed class ActivityLogManager : IDisposable
{
    public const int MaxEntries = 500;

    private readonly object _gate = new();
    private readonly string _filePath;
    private readonly List<RemoteFlowActivityEntry> _entries;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ActivityLogManager()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RemoteFlow");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "activity-log.json");
        _entries = Load();
    }

    public IReadOnlyList<RemoteFlowActivityEntry> Snapshot()
    {
        lock (_gate)
            return _entries.ToArray();
    }

    public void Add(string category, string source, string summary, string level = "Info")
    {
        if (_disposed || string.IsNullOrWhiteSpace(summary))
            return;

        var entry = new RemoteFlowActivityEntry(
            DateTimeOffset.UtcNow,
            Normalize(category),
            Normalize(source),
            Normalize(level),
            summary.Trim());

        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
            PersistLocked();
        }
    }

    public void Clear()
    {
        if (_disposed)
            return;

        lock (_gate)
        {
            _entries.Clear();
            PersistLocked();
        }
    }

    private List<RemoteFlowActivityEntry> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new List<RemoteFlowActivityEntry>();

            var json = File.ReadAllText(_filePath);
            var entries = JsonSerializer.Deserialize<List<RemoteFlowActivityEntry>>(json, JsonOptions) ?? new();
            return entries.Count > MaxEntries
                ? entries.Skip(entries.Count - MaxEntries).ToList()
                : entries;
        }
        catch
        {
            return new List<RemoteFlowActivityEntry>();
        }
    }

    private void PersistLocked()
    {
        try
        {
            var tempPath = _filePath + ".tmp";
            var json = JsonSerializer.Serialize(_entries, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, true);
        }
        catch
        {
            // The activity log must never interrupt RemoteFlow operations.
        }
    }

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Inconnu" : value.Trim();

    public void Dispose() => _disposed = true;
}
