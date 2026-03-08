using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FrameForge;

internal static class ImageSelectionHelper
{
    public static Rect NormalizeRect(Point start, Point end)
    {
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        var width = Math.Abs(end.X - start.X);
        var height = Math.Abs(end.Y - start.Y);
        return new Rect(x, y, width, height);
    }

    public static Int32Rect ToPixelRect(Rect dipRect, BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var scaleX = source.PixelWidth / Math.Max(1.0, source.Width);
        var scaleY = source.PixelHeight / Math.Max(1.0, source.Height);

        var x = (int)Math.Floor(dipRect.X * scaleX);
        var y = (int)Math.Floor(dipRect.Y * scaleY);
        var width = Math.Max(1, (int)Math.Ceiling(dipRect.Width * scaleX));
        var height = Math.Max(1, (int)Math.Ceiling(dipRect.Height * scaleY));

        var left = Math.Clamp(x, 0, source.PixelWidth);
        var top = Math.Clamp(y, 0, source.PixelHeight);
        var right = Math.Clamp(x + width, 0, source.PixelWidth);
        var bottom = Math.Clamp(y + height, 0, source.PixelHeight);

        var clippedWidth = Math.Max(0, right - left);
        var clippedHeight = Math.Max(0, bottom - top);

        if (clippedWidth == 0 || clippedHeight == 0)
        {
            return Int32Rect.Empty;
        }

        return new Int32Rect(left, top, clippedWidth, clippedHeight);
    }
}
