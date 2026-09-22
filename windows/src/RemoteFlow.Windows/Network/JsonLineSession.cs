using System.IO;
using System.Net.Sockets;
using System.Text;
using RemoteFlow.Windows.Core;

namespace RemoteFlow.Windows.Network;

public sealed class JsonLineSession : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonLineSession(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true;
        var stream = client.GetStream();
        _reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        _writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "
"
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
            var line = await _reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            await onLine(line);
        }
    }

    public ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        _writer.Dispose();
        _reader.Dispose();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
