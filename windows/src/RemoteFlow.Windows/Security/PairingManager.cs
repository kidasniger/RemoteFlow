using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;

namespace RemoteFlow.Windows.Security;

public sealed class PairingManager
{
    private const int PinLength = 6;
    private const int PinIterations = 120_000;
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("RemoteFlow|pairing|windows|v1");
    private readonly string _storageDirectory;
    private readonly string _storagePath;
    private readonly object _gate = new();
    private PairingStore _store;
    private ECDsa _identityKey;
    private readonly RemoteFlowCertificateManager _certificateManager;
    private readonly Dictionary<string, PairingAttemptState> _pairingAttempts = new(StringComparer.OrdinalIgnoreCase);

    public PairingManager()
    {
        _storageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RemoteFlow");
        _storagePath = Path.Combine(_storageDirectory, "pairing.json");
        Directory.CreateDirectory(_storageDirectory);

        _store = LoadStore();
        _identityKey = LoadOrCreateIdentityKey();
        _certificateManager = new RemoteFlowCertificateManager(DeviceId);
        EnsurePin();
        Save();
    }

    public string DeviceId => _store.DeviceId;
    public string Fingerprint => FormatFingerprint(Convert.FromBase64String(_store.PublicKeyBase64));
    public string PublicKeyBase64 => _store.PublicKeyBase64;
    public System.Security.Cryptography.X509Certificates.X509Certificate2 TlsCertificate => _certificateManager.Certificate;
    public string TlsFingerprint => _certificateManager.Fingerprint;
    public string CurrentPin => UnprotectString(_store.ProtectedPinBase64);
    public bool PairingEnforced => _store.PairingEnforced;
    public int PairedDeviceCount => _store.PairedDevices.Count;

    public string GetLocalIpv4Address()
    {
        try
        {
            var candidates = new List<(int Priority, string Address)>();

            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up ||
                    network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                var properties = network.GetIPProperties();
                var hasIpv4Gateway = properties.GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(gateway.Address) &&
                    !IsLinkLocal(gateway.Address));

                var interfacePriority = network.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 when hasIpv4Gateway => 0,
                    NetworkInterfaceType.Wireless80211 => 2,
                    NetworkInterfaceType.Ethernet when hasIpv4Gateway => 1,
                    NetworkInterfaceType.Ethernet => 3,
                    _ when hasIpv4Gateway => 4,
                    _ => 5
                };

                foreach (var address in properties.UnicastAddresses)
                {
                    var ipv4 = address.Address;
                    if (ipv4.AddressFamily != AddressFamily.InterNetwork ||
                        IPAddress.IsLoopback(ipv4) ||
                        IsLinkLocal(ipv4))
                        continue;

                    candidates.Add((interfacePriority, ipv4.ToString()));
                }
            }

            var selected = candidates
                .OrderBy(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.Address, StringComparer.Ordinal)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(selected.Address))
                return selected.Address;
        }
        catch
        {
        }

        return "127.0.0.1";
    }

    private static bool IsLinkLocal(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork &&
        address.GetAddressBytes() is { Length: 4 } bytes &&
        bytes[0] == 169 &&
        bytes[1] == 254;

    public string CreateQrPayload(int port)
    {
        return $"remoteflow://{GetLocalIpv4Address()}:{port}?device={Uri.EscapeDataString(DeviceId)}&tlsfp={TlsFingerprint}";
    }

    public BitmapImage CreateQrImage(int port)
    {
        var payload = CreateQrPayload(port);
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCoder.QRCodeGenerator.ECCLevel.Q);
        using var qr = new QRCoder.PngByteQRCode(data);
        var png = qr.GetGraphic(8);

        using var stream = new MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public RemoteFlowSecurityHello CreateSignedHello(int port)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var canonical = $"RemoteFlow|{RemoteFlow.Windows.Core.RemoteFlowProtocol.CurrentVersion}|{DeviceId}|{port}|{nonce}";
        var signature = _identityKey.SignData(
            Encoding.UTF8.GetBytes(canonical),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);

        return new RemoteFlowSecurityHello(
            DeviceId,
            Fingerprint,
            PublicKeyBase64,
            nonce,
            Convert.ToBase64String(signature),
            PairingEnforced,
            PinLength,
            "tls1.2+/p256+pin",
            TlsFingerprint);
    }

    public bool VerifyPin(string? pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
            return false;

        var normalized = new string(pin.Where(char.IsDigit).ToArray());
        if (normalized.Length != PinLength)
            return false;

        string saltBase64;
        string hashBase64;
        lock (_gate)
        {
            saltBase64 = _store.PinSaltBase64;
            hashBase64 = _store.PinHashBase64;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(saltBase64);
            expected = Convert.FromBase64String(hashBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        var candidate = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(normalized),
            salt,
            PinIterations,
            HashAlgorithmName.SHA256,
            32);

        return CryptographicOperations.FixedTimeEquals(candidate, expected);
    }

    public void RegeneratePin()
    {
        lock (_gate)
        {
            var pin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            StorePin(pin);
            Save();
        }
    }

    public bool TryBeginPairingAttempt(string? remoteKey)
    {
        if (string.IsNullOrWhiteSpace(remoteKey))
            return true;

        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            CleanupPairingAttempts(now);

            if (!_pairingAttempts.TryGetValue(remoteKey, out var state))
                return true;

            return state.BlockedUntilUtc is null || state.BlockedUntilUtc <= now;
        }
    }

    public void RecordPairingAttempt(string? remoteKey, bool success)
    {
        if (string.IsNullOrWhiteSpace(remoteKey))
            return;

        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            CleanupPairingAttempts(now);

            if (success)
            {
                _pairingAttempts.Remove(remoteKey);
                return;
            }

            if (!_pairingAttempts.TryGetValue(remoteKey, out var state) ||
                now - state.WindowStartedAtUtc >= TimeSpan.FromSeconds(RemoteFlowSecurityPolicy.PairingFailureWindowSeconds))
            {
                state = new PairingAttemptState(now, 0, null);
            }

            var failures = state.FailureCount + 1;
            var blockedUntil = failures >= RemoteFlowSecurityPolicy.MaxPairingFailures
                ? now.AddSeconds(RemoteFlowSecurityPolicy.PairingBlockSeconds)
                : state.BlockedUntilUtc;

            _pairingAttempts[remoteKey] = state with
            {
                FailureCount = failures,
                BlockedUntilUtc = blockedUntil
            };
        }
    }

    public bool TryPair(string? pin, string? clientDeviceId, string? clientName)
    {
        if (!VerifyPin(pin))
            return false;

        var normalizedId = string.IsNullOrWhiteSpace(clientDeviceId)
            ? $"android-{Guid.NewGuid():N}"
            : clientDeviceId.Trim();

        lock (_gate)
        {
            var existing = _store.PairedDevices.FirstOrDefault(x =>
                string.Equals(x.DeviceId, normalizedId, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                _store.PairedDevices.Add(new PairedDevice(
                    normalizedId,
                    string.IsNullOrWhiteSpace(clientName) ? "Android" : clientName.Trim(),
                    DateTimeOffset.UtcNow));
            }
            else
            {
                _store.PairedDevices[_store.PairedDevices.IndexOf(existing)] = existing with
                {
                    Name = string.IsNullOrWhiteSpace(clientName) ? existing.Name : clientName.Trim(),
                    LastPairedAtUtc = DateTimeOffset.UtcNow
                };
            }

            Save();
            return true;
        }
    }

    public bool IsPaired(string? clientDeviceId)
    {
        if (string.IsNullOrWhiteSpace(clientDeviceId))
            return false;

        lock (_gate)
        {
            return _store.PairedDevices.Any(x =>
                string.Equals(x.DeviceId, clientDeviceId.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    public void SetPairingEnforced(bool enabled)
    {
        lock (_gate)
        {
            _store.PairingEnforced = enabled;
            Save();
        }
    }

    private void EnsurePin()
    {
        if (!string.IsNullOrWhiteSpace(_store.ProtectedPinBase64) &&
            !string.IsNullOrWhiteSpace(_store.PinHashBase64) &&
            !string.IsNullOrWhiteSpace(_store.PinSaltBase64))
            return;

        StorePin(RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6"));
    }

    private void StorePin(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pin),
            salt,
            PinIterations,
            HashAlgorithmName.SHA256,
            32);

        _store.PinSaltBase64 = Convert.ToBase64String(salt);
        _store.PinHashBase64 = Convert.ToBase64String(hash);
        _store.ProtectedPinBase64 = Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(pin)));
    }

    private ECDsa LoadOrCreateIdentityKey()
    {
        if (!string.IsNullOrWhiteSpace(_store.ProtectedPrivateKeyBase64))
        {
            try
            {
                var key = ECDsa.Create();
                key.ImportPkcs8PrivateKey(
                    Unprotect(Convert.FromBase64String(_store.ProtectedPrivateKeyBase64)),
                    out _);
                return key;
            }
            catch
            {
                _store = CreateNewStore();
            }
        }

        var generated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = generated.ExportPkcs8PrivateKey();
        var publicKey = generated.ExportSubjectPublicKeyInfo();

        _store.ProtectedPrivateKeyBase64 = Convert.ToBase64String(Protect(privateKey));
        _store.PublicKeyBase64 = Convert.ToBase64String(publicKey);
        _store.DeviceId = Guid.NewGuid().ToString("N");
        return generated;
    }

    private PairingStore LoadStore()
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                var json = File.ReadAllText(_storagePath);
                var store = JsonSerializer.Deserialize<PairingStore>(json, JsonOptions);
                if (store is not null &&
                    !string.IsNullOrWhiteSpace(store.DeviceId) &&
                    !string.IsNullOrWhiteSpace(store.PublicKeyBase64))
                    return store;
            }
        }
        catch
        {
        }

        return CreateNewStore();
    }

    private void CleanupPairingAttempts(DateTimeOffset now)
    {
        foreach (var entry in _pairingAttempts.ToArray())
        {
            var expiredWindow = now - entry.Value.WindowStartedAtUtc >= TimeSpan.FromSeconds(RemoteFlowSecurityPolicy.PairingFailureWindowSeconds);
            var expiredBlock = entry.Value.BlockedUntilUtc is null || entry.Value.BlockedUntilUtc <= now;
            if (expiredWindow && expiredBlock)
                _pairingAttempts.Remove(entry.Key);
        }
    }

    private sealed record PairingAttemptState(
        DateTimeOffset WindowStartedAtUtc,
        int FailureCount,
        DateTimeOffset? BlockedUntilUtc);

    private static PairingStore CreateNewStore() => new();

    private void Save()
    {
        lock (_gate)
        {
            var json = JsonSerializer.Serialize(_store, JsonOptions);
            var temp = _storagePath + ".tmp";
            File.WriteAllText(temp, json, Encoding.UTF8);
            File.Move(temp, _storagePath, true);
        }
    }

    private static byte[] Protect(byte[] data) =>
        ProtectedData.Protect(data, DpapiEntropy, DataProtectionScope.CurrentUser);

    private static byte[] Unprotect(byte[] data) =>
        ProtectedData.Unprotect(data, DpapiEntropy, DataProtectionScope.CurrentUser);

    private static string UnprotectString(string base64) =>
        Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(base64)));

    private static string FormatFingerprint(byte[] publicKey)
    {
        using var sha = SHA256.Create();
        var digest = sha.ComputeHash(publicKey);
        return string.Join(":", digest.Select(b => b.ToString("X2")));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record RemoteFlowSecurityHello(
    string DeviceId,
    string Fingerprint,
    string PublicKey,
    string Nonce,
    string Signature,
    bool PairingRequired,
    int PinLength,
    string Security,
    string TlsFingerprint);

public sealed record PairedDevice(
    string DeviceId,
    string Name,
    DateTimeOffset LastPairedAtUtc);

public sealed class PairingStore
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string PublicKeyBase64 { get; set; } = string.Empty;
    public string ProtectedPrivateKeyBase64 { get; set; } = string.Empty;
    public string PinSaltBase64 { get; set; } = string.Empty;
    public string PinHashBase64 { get; set; } = string.Empty;
    public string ProtectedPinBase64 { get; set; } = string.Empty;
    public bool PairingEnforced { get; set; } = true;
    public List<PairedDevice> PairedDevices { get; set; } = new();
}
