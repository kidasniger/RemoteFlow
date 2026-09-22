using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RemoteFlow.Windows.Screen;

public sealed record CapturedFrame(byte[] JpegBytes, int Width, int Height);

public static class DesktopCapture
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;
    private const uint DIB_RGB_COLORS = 0;
    private const uint BI_RGB = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hDC);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hDC, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hDC, nint hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hDC);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        nint hdcDest,
        int nXDest,
        int nYDest,
        int nWidth,
        int nHeight,
        nint hdcSrc,
        int nXSrc,
        int nYSrc,
        int dwRop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        nint hdc,
        nint hbmp,
        uint uStartScan,
        uint cScanLines,
        byte[] lpvBits,
        ref BITMAPINFO lpbi,
        uint uUsage);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public static CapturedFrame CaptureJpeg(int maxWidth = 1280, int quality = 60)
    {
        var x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return CaptureJpeg(x, y, width, height, maxWidth, quality);
    }

    public static CapturedFrame CaptureJpeg(
        int x,
        int y,
        int width,
        int height,
        int maxWidth = 1280,
        int quality = 60)
    {
        maxWidth = Math.Clamp(maxWidth, 480, 2560);
        quality = Math.Clamp(quality, 30, 90);

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Zone de capture Windows indisponible.");

        var screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
            throw new InvalidOperationException("Impossible d'accéder au bureau Windows.");

        var memDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, width, height);
        nint previous = nint.Zero;

        try
        {
            if (memDc == nint.Zero || bitmap == nint.Zero)
                throw new InvalidOperationException("Allocation de capture écran impossible.");

            previous = SelectObject(memDc, bitmap);
            if (previous == nint.Zero)
                throw new InvalidOperationException("Préparation du tampon de capture impossible.");

            if (!BitBlt(memDc, 0, 0, width, height, screenDc, x, y, SRCCOPY | CAPTUREBLT))
                throw new InvalidOperationException("La capture écran a échoué.");

            var pixels = new byte[checked(width * height * 4)];
            var info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB
                }
            };

            if (GetDIBits(memDc, bitmap, 0, (uint)height, pixels, ref info, DIB_RGB_COLORS) == 0)
                throw new InvalidOperationException("Lecture de l'image capturée impossible.");

            var source = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                width * 4);
            source.Freeze();

            BitmapSource output = source;
            if (width > maxWidth)
            {
                var scaledHeight = Math.Max(1, (int)Math.Round(height * (double)maxWidth / width));
                var transformed = new TransformedBitmap(
                    source,
                    new System.Windows.Media.ScaleTransform(
                        maxWidth / (double)width,
                        scaledHeight / (double)height));
                transformed.Freeze();
                output = transformed;
            }

            using var stream = new MemoryStream();
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(output));
            encoder.Save(stream);

            return new CapturedFrame(
                stream.ToArray(),
                output.PixelWidth,
                output.PixelHeight);
        }
        finally
        {
            if (memDc != nint.Zero && previous != nint.Zero)
                SelectObject(memDc, previous);
            if (bitmap != nint.Zero)
                DeleteObject(bitmap);
            if (memDc != nint.Zero)
                DeleteDC(memDc);
            ReleaseDC(nint.Zero, screenDc);
        }
    }
}
