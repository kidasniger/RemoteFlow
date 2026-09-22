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

    public PairingManager()
    {
        _storageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RemoteFlow");
        _storagePath = Path.Combine(_storageDirectory, "pairing.json");
        Directory.CreateDirectory(_storageDirectory);

        _store = LoadStore();
        _identityKey = LoadOrCreateIdentityKey();
        EnsurePin();
        Save();
    }

    public string DeviceId => _store.DeviceId;
    public string Fingerprint => FormatFingerprint(Convert.FromBase64String(_store.PublicKeyBase64));
    public string PublicKeyBase64 => _store.PublicKeyBase64;
    public string CurrentPin => UnprotectString(_store.ProtectedPinBase64);
    public bool PairingEnforced => _store.PairingEnforced;
    public int PairedDeviceCount => _store.PairedDevices.Count;

    public string GetLocalIpv4Address()
    {
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up ||
                    network.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    network.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                foreach (var address in network.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address.Address))
                        return address.Address.ToString();
                }
            }
        }
        catch
        {
        }

        return "127.0.0.1";
    }

    public string CreateQrPayload(int port)
    {
        return $"remoteflow://{GetLocalIpv4Address()}:{port}";
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
            "identity+p256");
    }

    public bool VerifyPin(string? pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
            return false;

        var normalized = new string(pin.Where(char.IsDigit).ToArray());
        if (normalized.Length != PinLength)
            return false;

        var candidate = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(normalized),
            Convert.FromBase64String(_store.PinSaltBase64),
            PinIterations,
            HashAlgorithmName.SHA256,
            32);

        var expected = Convert.FromBase64String(_store.PinHashBase64);
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
    string Security);

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
    public bool PairingEnforced { get; set; }
    public List<PairedDevice> PairedDevices { get; set; } = new();
}
