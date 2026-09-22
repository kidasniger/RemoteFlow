using System.Text.Json;
using RemoteFlow.Windows.Input;

namespace RemoteFlow.Windows.Macros;

public sealed record RemoteFlowMacroStep(
    string Action,
    string? Type = null,
    string? Key = null,
    string? Text = null,
    float? X = null,
    float? Y = null,
    float? Dx = null,
    float? Dy = null,
    int DelayMs = 0)
{
    public string Summary => Action.ToLowerInvariant() switch
    {
        "keyboard" => $"Clavier : {Key ?? "(vide)"}",
        "text" => $"Texte : {(Text ?? string.Empty).Replace("\r", string.Empty).Replace("\n", " ↵ ")}",
        "mouse" => $"Souris : {Type ?? "(vide)"}",
        "delay" => $"Attente : {DelayMs} ms",
        _ => $"Action : {Action}"
    };
}

public sealed record RemoteFlowMacro(
    string Id,
    string Name,
    IReadOnlyList<RemoteFlowMacroStep> Steps,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public int StepCount => Steps.Count;
}

public sealed class MacroManager
{
    private const int MaxMacros = 100;
    private const int MaxStepsPerMacro = 200;
    private const int MaxDelayMs = 60000;
    private const int MaxTextLength = 10000;

    private readonly object _gate = new();
    private readonly string _filePath;
    private List<RemoteFlowMacro> _macros;

    public MacroManager()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RemoteFlow");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "macros.json");
        _macros = Load();
    }

    public IReadOnlyList<RemoteFlowMacro> List()
    {
        lock (_gate)
        {
            return _macros
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(Clone)
                .ToArray();
        }
    }

    public RemoteFlowMacro Save(string? id, string name, IEnumerable<RemoteFlowMacroStep> steps)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length is < 1 or > 80)
            throw new InvalidOperationException("Le nom de la macro doit contenir entre 1 et 80 caractères.");

        var sanitized = steps?.ToArray() ?? Array.Empty<RemoteFlowMacroStep>();
        if (sanitized.Length == 0)
            throw new InvalidOperationException("Une macro doit contenir au moins une étape.");
        if (sanitized.Length > MaxStepsPerMacro)
            throw new InvalidOperationException($"Une macro ne peut pas dépasser {MaxStepsPerMacro} étapes.");

        ValidateSteps(sanitized);

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            RemoteFlowMacro saved;

            if (!string.IsNullOrWhiteSpace(id))
            {
                var index = _macros.FindIndex(x => x.Id == id);
                if (index < 0)
                    throw new KeyNotFoundException("Macro introuvable.");

                var current = _macros[index];
                saved = new RemoteFlowMacro(current.Id, name, sanitized, current.CreatedAtUtc, now);
                _macros[index] = saved;
            }
            else
            {
                if (_macros.Count >= MaxMacros)
                    throw new InvalidOperationException($"Limite atteinte : {MaxMacros} macros.");

                saved = new RemoteFlowMacro(
                    Guid.NewGuid().ToString("N"),
                    name,
                    sanitized,
                    now,
                    now);
                _macros.Add(saved);
            }

            PersistLocked();
            return Clone(saved);
        }
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            var removed = _macros.RemoveAll(x =>
                string.Equals(x.Id, id, StringComparison.Ordinal)) > 0;

            if (removed)
                PersistLocked();

            return removed;
        }
    }

    public async Task ExecuteAsync(string id, CancellationToken cancellationToken = default)
    {
        RemoteFlowMacro macro;
        lock (_gate)
        {
            macro = _macros.FirstOrDefault(x => x.Id == id)
                ?? throw new KeyNotFoundException("Macro introuvable.");
        }

        foreach (var step in macro.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ok = step.Action.ToLowerInvariant() switch
            {
                "keyboard" => string.IsNullOrWhiteSpace(step.Key)
                    ? false
                    : step.Key.Contains('+', StringComparison.Ordinal)
                        ? WindowsKeyboardController.PressChord(step.Key)
                        : WindowsKeyboardController.PressLabel(step.Key),
                "text" => WindowsKeyboardController.TypeText(step.Text),
                "mouse" => WindowsMouseController.Execute(
                    step.Type,
                    step.X,
                    step.Y,
                    step.Dx,
                    step.Dy),
                "delay" => true,
                _ => false
            };

            if (!ok)
                throw new InvalidOperationException($"Étape de macro non exécutée : {step.Summary}");

            if (step.DelayMs > 0)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(Math.Min(step.DelayMs, MaxDelayMs)),
                    cancellationToken);
            }
        }
    }

    private static void ValidateSteps(IEnumerable<RemoteFlowMacroStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.DelayMs is < 0 or > MaxDelayMs)
                throw new InvalidOperationException("Chaque pause doit être comprise entre 0 et 60000 ms.");

            switch (step.Action.ToLowerInvariant())
            {
                case "keyboard":
                    if (string.IsNullOrWhiteSpace(step.Key) || step.Key.Length > 100)
                        throw new InvalidOperationException("Touche ou raccourci clavier invalide.");
                    break;

                case "text":
                    if (step.Text is null || step.Text.Length > MaxTextLength)
                        throw new InvalidOperationException($"Un texte de macro ne peut pas dépasser {MaxTextLength} caractères.");
                    break;

                case "mouse":
                    if (string.IsNullOrWhiteSpace(step.Type))
                        throw new InvalidOperationException("Commande souris manquante.");

                    var type = step.Type.Trim().ToUpperInvariant();
                    if (type is not ("MOVE" or "MOVE_RELATIVE" or "LEFT_CLICK" or "RIGHT_CLICK" or "DOUBLE_CLICK" or "SCROLL_UP" or "SCROLL_DOWN"))
                        throw new InvalidOperationException($"Commande souris non autorisée : {type}");

                    if (type == "MOVE" && (step.X is null or < 0f or > 1f || step.Y is null or < 0f or > 1f))
                        throw new InvalidOperationException("MOVE nécessite X et Y entre 0 et 1.");

                    if (type == "MOVE_RELATIVE" && (step.Dx is null || step.Dy is null))
                        throw new InvalidOperationException("MOVE_RELATIVE nécessite DX et DY.");
                    break;

                case "delay":
                    if (step.DelayMs is < 1 or > MaxDelayMs)
                        throw new InvalidOperationException("Une attente doit être comprise entre 1 et 60000 ms.");
                    break;

                default:
                    throw new InvalidOperationException($"Action de macro non autorisée : {step.Action}");
            }
        }
    }

    private List<RemoteFlowMacro> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new List<RemoteFlowMacro>();

            var json = File.ReadAllText(_filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var macros = JsonSerializer.Deserialize<List<RemoteFlowMacro>>(json, options)
                ?? new List<RemoteFlowMacro>();

            return macros
                .Take(MaxMacros)
                .Where(IsValidMacro)
                .Select(Clone)
                .ToList();
        }
        catch
        {
            return new List<RemoteFlowMacro>();
        }
    }

    private void PersistLocked()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_macros, options));
        File.Move(tempPath, _filePath, true);
    }

    private static bool IsValidMacro(RemoteFlowMacro macro)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(macro.Id) ||
                macro.Name.Trim().Length is < 1 or > 80 ||
                macro.Steps.Count is < 1 or > MaxStepsPerMacro)
                return false;

            ValidateSteps(macro.Steps);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static RemoteFlowMacro Clone(RemoteFlowMacro macro) =>
        new(
            macro.Id,
            macro.Name,
            macro.Steps.Select(Clone).ToArray(),
            macro.CreatedAtUtc,
            macro.UpdatedAtUtc);

    private static RemoteFlowMacroStep Clone(RemoteFlowMacroStep step) =>
        new(
            step.Action,
            step.Type,
            step.Key,
            step.Text,
            step.X,
            step.Y,
            step.Dx,
            step.Dy,
            step.DelayMs);
}
