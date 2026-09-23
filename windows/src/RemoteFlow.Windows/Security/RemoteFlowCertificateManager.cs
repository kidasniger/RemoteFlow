using System.Security.Cryptography;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace RemoteFlow.Windows.Security;

public sealed class RemoteFlowCertificateManager : IDisposable
{
    private static readonly byte[] DpapiEntropy =
        Encoding.UTF8.GetBytes("RemoteFlow|tls|windows|v1");

    private readonly string _path;
    private readonly X509Certificate2 _certificate;

    public RemoteFlowCertificateManager(string deviceId)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RemoteFlow");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "tls-certificate.pfx");
        _certificate = LoadOrCreate(deviceId);
    }

    public X509Certificate2 Certificate => _certificate;

    public string Fingerprint =>
        Convert.ToHexString(SHA256.HashData(_certificate.RawData));

    private X509Certificate2 LoadOrCreate(string deviceId)
    {
        if (File.Exists(_path))
        {
            try
            {
                var protectedBytes = Convert.FromBase64String(
                    File.ReadAllText(_path, Encoding.UTF8));
                var pfx = ProtectedData.Unprotect(
                    protectedBytes,
                    DpapiEntropy,
                    DataProtectionScope.CurrentUser);

                return new X509Certificate2(
                    pfx,
                    string.Empty,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
            }
            catch
            {
                try { File.Delete(_path); } catch { }
            }
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            $"CN=RemoteFlow-{deviceId}",
            key,
            HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,
                critical: true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using var generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(5));

        var pfxBytes = generated.Export(X509ContentType.Pkcs12, string.Empty);
        var protectedBytesNew = ProtectedData.Protect(
            pfxBytes,
            DpapiEntropy,
            DataProtectionScope.CurrentUser);

        var tempPath = _path + ".tmp";
        File.WriteAllText(
            tempPath,
            Convert.ToBase64String(protectedBytesNew),
            new UTF8Encoding(false));
        File.Move(tempPath, _path, true);

        return new X509Certificate2(
            pfxBytes,
            string.Empty,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
    }

    public void Dispose() => _certificate.Dispose();
}
