using System.IO;
using System.Net.Sockets;
using RemoteFlow.Windows.Network;

namespace RemoteFlow.Windows.Screen;

public sealed class DesktopStreamController : IAsyncDisposable
{
    private readonly JsonLineSession _session;
    private readonly CancellationToken _sessionCancellation;
    private CancellationTokenSource? _streamCts;
    private Task? _streamTask;

    public DesktopStreamController(JsonLineSession session, CancellationToken sessionCancellation)
    {
        _session = session;
        _sessionCancellation = sessionCancellation;
    }

    public async Task<string> StartAsync(int fps, int maxWidth, int quality, CancellationToken cancellationToken)
    {
        await StopAsync();
        fps = Math.Clamp(fps, 1, 15);
        maxWidth = Math.Clamp(maxWidth, 480, 2560);
        quality = Math.Clamp(quality, 30, 90);
        _streamCts = new CancellationTokenSource();
        var streamer = new DesktopStreamer(_session, fps, maxWidth, quality, _sessionCancellation);
        _streamTask = streamer.RunAsync(_streamCts.Token);
        await _session.SendAsync(new RemoteFlowStreamState(
            Event: "screen_stream", State: "started", Fps: fps, MaxWidth: maxWidth,
            Quality: quality, FrameFormat: "jpeg"), cancellationToken);
        return $"Streaming bureau demarre • {fps} FPS • {maxWidth}px • JPEG Q{quality}";
    }

    public async Task<string> StopAsync()
    {
        _streamCts?.Cancel();
        if (_streamTask is not null)
        {
            try { await _streamTask; }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (SocketException) { }
        }
        _streamTask = null;
        _streamCts?.Dispose();
        _streamCts = null;
        return "Streaming bureau arrete";
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}

public sealed class DesktopStreamer
{
    private readonly JsonLineSession _session;
    private readonly int _fps;
    private readonly int _maxWidth;
    private readonly int _quality;
    private readonly CancellationToken _sessionCancellation;
    private long _sequence;

    public DesktopStreamer(JsonLineSession session, int fps, int maxWidth, int quality, CancellationToken sessionCancellation)
    {
        _session = session;
        _fps = Math.Clamp(fps, 1, 15);
        _maxWidth = Math.Clamp(maxWidth, 480, 2560);
        _quality = Math.Clamp(quality, 30, 90);
        _sessionCancellation = sessionCancellation;
    }

    public async Task RunAsync(CancellationToken streamCancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(streamCancellation, _sessionCancellation);
        var token = linked.Token;
        var interval = TimeSpan.FromMilliseconds(1000d / _fps);

        while (!token.IsCancellationRequested)
        {
            var started = DateTimeOffset.UtcNow;
            try
            {
                var frame = await Task.Run(() => DesktopCapture.CaptureJpeg(_maxWidth, _quality), token);
                await _session.SendAsync(new RemoteFlowScreenFrame(
                    Event: "screen_frame",
                    Sequence: Interlocked.Increment(ref _sequence),
                    Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Width: frame.Width, Height: frame.Height, Format: "jpeg", Quality: _quality,
                    Data: Convert.ToBase64String(frame.JpegBytes)), token);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { break; }
            catch (SocketException) { break; }

            var delay = interval - (DateTimeOffset.UtcNow - started);
            if (delay > TimeSpan.Zero)
            {
                try { await Task.Delay(delay, token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}

public sealed record RemoteFlowScreenFrame(
    string Event, long Sequence, long Timestamp, int Width, int Height,
    string Format, int Quality, string Data);

public sealed record RemoteFlowStreamState(
    string Event, string State, int Fps, int MaxWidth, int Quality, string FrameFormat);