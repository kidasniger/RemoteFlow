using System.IO;
using OpenCvSharp;
using RemoteFlow.Windows.Core;

namespace RemoteFlow.Windows.Webcam;

public sealed record WebcamDeviceInfo(int Index, string Name);

public sealed class WebcamManager : IAsyncDisposable
{
    private readonly object _gate = new();
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private WebcamDeviceInfo? _currentDevice;

    public bool IsRunning
    {
        get { lock (_gate) return _captureTask is not null; }
    }

    public event EventHandler<RemoteFlowWebcamFrame>? FrameCaptured;
    public event EventHandler<string>? StatusChanged;

    public static IReadOnlyList<WebcamDeviceInfo> DetectDevices(int maxDevices = 8)
    {
        var devices = new List<WebcamDeviceInfo>();

        for (var index = 0; index < Math.Clamp(maxDevices, 1, 16); index++)
        {
            using var capture = OpenDevice(index);
            if (capture is null)
                continue;

            devices.Add(new WebcamDeviceInfo(index, $"Webcam {index + 1}"));
        }

        return devices;
    }

    public async Task<string> StartAsync(
        int cameraIndex,
        int width,
        int height,
        int fps,
        int quality,
        CancellationToken cancellationToken = default)
    {
        await StopAsync();

        width = Math.Clamp(width, 320, 1920);
        height = Math.Clamp(height, 240, 1080);
        fps = Math.Clamp(fps, 1, 30);
        quality = Math.Clamp(quality, 40, 95);

        var capture = OpenDevice(cameraIndex);
        if (capture is null)
            throw new InvalidOperationException($"Webcam #{cameraIndex + 1} indisponible.");

        try
        {
            capture.Set(VideoCaptureProperties.FrameWidth, width);
            capture.Set(VideoCaptureProperties.FrameHeight, height);
            capture.Set(VideoCaptureProperties.Fps, fps);
            capture.Set(VideoCaptureProperties.BufferSize, 1);

            using var probe = new Mat();
            if (!capture.Read(probe) || probe.Empty())
                throw new InvalidOperationException($"Impossible de lire une image depuis la webcam #{cameraIndex + 1}.");

            var actualWidth = probe.Width > 0 ? probe.Width : width;
            var actualHeight = probe.Height > 0 ? probe.Height : height;

            CancellationTokenSource cts;
            lock (_gate)
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _capture = capture;
                _cts = cts;
                _currentDevice = new WebcamDeviceInfo(cameraIndex, $"Webcam {cameraIndex + 1}");
                _captureTask = CaptureLoopAsync(capture, cts, fps, quality);
            }

            StatusChanged?.Invoke(
                this,
                $"Webcam {cameraIndex + 1} active • {actualWidth}×{actualHeight} • {fps} FPS • JPEG Q{quality}");

            return $"Webcam {cameraIndex + 1} démarrée • {actualWidth}×{actualHeight} • {fps} FPS • JPEG Q{quality}";
        }
        catch
        {
            capture.Dispose();
            throw;
        }
    }

    public async Task<string> StopAsync()
    {
        CancellationTokenSource? cts;
        Task? task;

        lock (_gate)
        {
            cts = _cts;
            task = _captureTask;
            if (cts is null && task is null)
                return "Webcam déjà arrêtée";
            cts?.Cancel();
        }

        if (task is not null)
        {
            try { await task; }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }

        lock (_gate)
        {
            _capture = null;
            _cts?.Dispose();
            _cts = null;
            _captureTask = null;
            _currentDevice = null;
        }

        StatusChanged?.Invoke(this, "Webcam arrêtée");
        return "Webcam arrêtée";
    }

    private async Task CaptureLoopAsync(
        VideoCapture capture,
        CancellationTokenSource cts,
        int fps,
        int quality)
    {
        var token = cts.Token;
        var interval = TimeSpan.FromMilliseconds(1000d / fps);
        long sequence = 0;

        try
        {
            using var frame = new Mat();

            while (!token.IsCancellationRequested)
            {
                var started = DateTimeOffset.UtcNow;

                if (!capture.Read(frame) || frame.Empty())
                    throw new IOException("La webcam n'a pas fourni d'image.");

                Cv2.ImEncode(
                    ".jpg",
                    frame,
                    out var encoded,
                    new ImageEncodingParam(ImwriteFlags.JpegQuality, quality));

                var current = _currentDevice ?? new WebcamDeviceInfo(0, "Webcam");
                FrameCaptured?.Invoke(
                    this,
                    new RemoteFlowWebcamFrame(
                        Event: "webcam_frame",
                        Sequence: Interlocked.Increment(ref sequence),
                        Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Width: frame.Width,
                        Height: frame.Height,
                        Fps: fps,
                        Quality: quality,
                        Format: "jpeg",
                        Data: Convert.ToBase64String(encoded)));

                var delay = interval - (DateTimeOffset.UtcNow - started);
                if (delay > TimeSpan.Zero)
                {
                    try { await Task.Delay(delay, token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Webcam interrompue : {ex.Message}");
        }
        finally
        {
            try { capture.Release(); } catch { }
            try { capture.Dispose(); } catch { }
        }
    }

    private static VideoCapture? OpenDevice(int index)
    {
        if (index < 0)
            return null;

        foreach (var backend in new[]
        {
            VideoCaptureAPIs.DSHOW,
            VideoCaptureAPIs.MSMF,
            VideoCaptureAPIs.ANY
        })
        {
            try
            {
                var capture = new VideoCapture(index, backend);
                if (capture.IsOpened())
                    return capture;
                capture.Dispose();
            }
            catch
            {
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); } catch { }
    }
}
