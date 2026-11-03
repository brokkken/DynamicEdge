using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicEdge.Services;

public static class ImageUtil
{
    public static BitmapImage? FromBytes(byte[] data)
    {
        if (data.Length == 0) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = 256;
            bi.StreamSource = new MemoryStream(data);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    /// Насыщенный «средний» цвет обложки для акцента.
    public static Color Dominant(BitmapSource src)
    {
        try
        {
            var small = new TransformedBitmap(src, new ScaleTransform(16.0 / src.PixelWidth, 16.0 / src.PixelHeight));
            var fc = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int w = fc.PixelWidth, h = fc.PixelHeight;
            var px = new byte[w * h * 4];
            fc.CopyPixels(px, w * 4, 0);
            double r = 0, g = 0, b = 0, ws = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                double B = px[i], G = px[i + 1], R = px[i + 2];
                double mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B));
                double sat = mx <= 0 ? 0 : (mx - mn) / mx;
                double wt = 0.05 + sat * sat * (mx / 255.0);
                r += R * wt; g += G * wt; b += B * wt; ws += wt;
            }
            if (ws <= 0) return Core.ThemeManager.DefaultAccent;
            r /= ws; g /= ws; b /= ws;
            double top = Math.Max(r, Math.Max(g, b));
            if (top < 1) return Core.ThemeManager.DefaultAccent;
            double k = top < 170 ? 170 / top : 1;
            return Color.FromRgb((byte)Math.Min(255, r * k), (byte)Math.Min(255, g * k), (byte)Math.Min(255, b * k));
        }
        catch { return Core.ThemeManager.DefaultAccent; }
    }
}
