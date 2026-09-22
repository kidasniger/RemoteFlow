using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RemoteFlow.Windows.Clipboard;

public sealed class ClipboardChangedEventArgs : EventArgs
{
    public ClipboardChangedEventArgs(string text) => Text = text;
    public string Text { get; }
}

public sealed class ClipboardSyncManager : IDisposable
{
    public const int MaxTextLength = 1_000_000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);

    private readonly object _gate = new();
    private readonly Timer _pollTimer;
    private string _lastObservedHash;
    private bool _disposed;

    public event EventHandler<ClipboardChangedEventArgs>? Changed;

    public ClipboardSyncManager()
    {
        _lastObservedHash = ComputeHash(WindowsClipboard.TryReadText());
        _pollTimer = new Timer(PollClipboard, null, PollInterval, PollInterval);
    }

    public string? ReadText() => WindowsClipboard.TryReadText();

    public bool TrySetText(string text)
    {
        if (_disposed || text.Length > MaxTextLength)
            return false;

        lock (_gate)
        {
            if (_disposed)
                return false;

            if (!WindowsClipboard.TryWriteText(text))
                return false;

            _lastObservedHash = ComputeHash(text);
            return true;
        }
    }

    private void PollClipboard(object? state)
    {
        if (_disposed)
            return;

        string? text;
        bool changed;

        lock (_gate)
        {
            if (_disposed)
                return;

            text = WindowsClipboard.TryReadText();
            var hash = ComputeHash(text);
            changed = !string.Equals(hash, _lastObservedHash, StringComparison.Ordinal);
            if (changed)
                _lastObservedHash = hash;
        }

        if (changed && text is not null && text.Length <= MaxTextLength)
            Changed?.Invoke(this, new ClipboardChangedEventArgs(text));
    }

    private static string ComputeHash(string? text)
    {
        if (text is null)
            return string.Empty;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        _pollTimer.Dispose();
    }

    private static class WindowsClipboard
    {
        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        public static string? TryReadText()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (!OpenClipboard(IntPtr.Zero))
                {
                    Thread.Sleep(12);
                    continue;
                }

                try
                {
                    if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
                        return null;

                    var handle = GetClipboardData(CF_UNICODETEXT);
                    if (handle == IntPtr.Zero)
                        return null;

                    var pointer = GlobalLock(handle);
                    if (pointer == IntPtr.Zero)
                        return null;

                    try
                    {
                        return Marshal.PtrToStringUni(pointer);
                    }
                    finally
                    {
                        GlobalUnlock(handle);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }

            return null;
        }

        public static bool TryWriteText(string text)
        {
            var bytes = Encoding.Unicode.GetBytes(text + "\0");
            var memory = IntPtr.Zero;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (!OpenClipboard(IntPtr.Zero))
                {
                    Thread.Sleep(12);
                    continue;
                }

                try
                {
                    if (!EmptyClipboard())
                        return false;

                    memory = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                    if (memory == IntPtr.Zero)
                        return false;

                    var pointer = GlobalLock(memory);
                    if (pointer == IntPtr.Zero)
                        return false;

                    try
                    {
                        Marshal.Copy(bytes, 0, pointer, bytes.Length);
                    }
                    finally
                    {
                        GlobalUnlock(memory);
                    }

                    var result = SetClipboardData(CF_UNICODETEXT, memory);
                    if (result == IntPtr.Zero)
                        return false;

                    memory = IntPtr.Zero;
                    return true;
                }
                finally
                {
                    if (memory != IntPtr.Zero)
                        GlobalFree(memory);
                    CloseClipboard();
                }
            }

            return false;
        }
    }
}
