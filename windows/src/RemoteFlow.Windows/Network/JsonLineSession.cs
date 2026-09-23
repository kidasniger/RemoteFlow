using System.IO;
using System.Net.Sockets;
using System.Text;
using RemoteFlow.Windows.Core;
using RemoteFlow.Windows.Security;

namespace RemoteFlow.Windows.Network;

public sealed class JsonLineSession : IAsyncDisposable
{
    private const int ReadBufferSize = 4096;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private readonly TcpClient _client;
    private readonly Stream _stream;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly byte[] _readBuffer = new byte[ReadBufferSize];
    private int _readStart;
    private int _readEnd;

    public JsonLineSession(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true;
        _stream = client.GetStream();
        _writer = new StreamWriter(_stream, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };
    }

    public string RemoteEndpoint => _client.Client.RemoteEndPoint?.ToString() ?? "unknown";

    public async Task SendAsync(object message, CancellationToken cancellationToken = default)
    {
        var json = RemoteFlowProtocol.Serialize(message);
        await _writeLock.WaitAsync(cancellationToken);
        try { await _writer.WriteLineAsync(json); }
        finally { _writeLock.Release(); }
    }

    public async Task RunAsync(Func<string, Task> onLine, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await ReadLineAsync(cancellationToken);
            if (line is null)
                break;
            await onLine(line);
        }
    }

    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        using var line = new MemoryStream(capacity: Math.Min(RemoteFlowSecurityPolicy.MaxIncomingLineBytes, 16 * 1024));

        while (true)
        {
            for (var index = _readStart; index < _readEnd; index++)
            {
                if (_readBuffer[index] != (byte)'\n')
                    continue;

                var count = index - _readStart;
                if (count > 0 && _readBuffer[index - 1] == (byte)'\r')
                    count--;

                AppendBytes(line, _readBuffer, _readStart, count);
                _readStart = index + 1;
                return DecodeLine(line);
            }

            if (_readStart < _readEnd)
            {
                AppendBytes(line, _readBuffer, _readStart, _readEnd - _readStart);
                _readStart = _readEnd;
            }

            var read = await _stream.ReadAsync(_readBuffer.AsMemory(0, _readBuffer.Length), cancellationToken);
            if (read == 0)
            {
                if (line.Length == 0)
                    return null;
                return DecodeLine(line);
            }

            _readStart = 0;
            _readEnd = read;
        }
    }

    private static void AppendBytes(MemoryStream target, byte[] source, int offset, int count)
    {
        if (count <= 0)
            return;

        if (target.Length + count > RemoteFlowSecurityPolicy.MaxIncomingLineBytes)
            throw new InvalidDataException($"Message JSON trop volumineux (maximum {RemoteFlowSecurityPolicy.MaxIncomingLineBytes:N0} octets).");

        target.Write(source, offset, count);
    }

    private static string DecodeLine(MemoryStream line)
    {
        var bytes = line.ToArray();
        try
        {
            return Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("Encodage UTF-8 invalide.", ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        _writer.Dispose();
        _stream.Dispose();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
