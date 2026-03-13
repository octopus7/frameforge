using System;
using System.Collections.Generic;
using System.Windows;

namespace FrameForge;

internal static class CanvasLayoutHelper
{
    public const double PreviewPadding = 20;

    public static Point CenterFrame(int canvasWidth, int canvasHeight, int frameWidth, int frameHeight)
    {
        var x = (int)Math.Floor((canvasWidth - frameWidth) / 2.0);
        var y = (int)Math.Floor((canvasHeight - frameHeight) / 2.0);
        return new Point(x, y);
    }

    public static CanvasViewportLayout CalculateViewport(int canvasWidth, int canvasHeight, AnimationFrame? currentFrame, double padding = PreviewPadding)
    {
        if (canvasWidth > 0 && canvasHeight > 0)
        {
            var canvasRect = new Rect(padding, padding, canvasWidth, canvasHeight);
            var frameRect = currentFrame is null
                ? Rect.Empty
                : new Rect(
                    canvasRect.X + currentFrame.X,
                    canvasRect.Y + currentFrame.Y,
                    currentFrame.Image.PixelWidth,
                    currentFrame.Image.PixelHeight);

            return new CanvasViewportLayout(
                Math.Max(1, canvasWidth + (padding * 2)),
                Math.Max(1, canvasHeight + (padding * 2)),
                canvasRect,
                frameRect);
        }

        if (currentFrame is null)
        {
            return CanvasViewportLayout.Empty;
        }

        return new CanvasViewportLayout(
            Math.Max(1, currentFrame.Image.PixelWidth + (padding * 2)),
            Math.Max(1, currentFrame.Image.PixelHeight + (padding * 2)),
            Rect.Empty,
            new Rect(padding, padding, currentFrame.Image.PixelWidth, currentFrame.Image.PixelHeight));
    }

    public static Int32Rect ClampSelectionToCanvas(Rect selectionRect, Rect canvasRect, int canvasWidth, int canvasHeight)
    {
        if (selectionRect.IsEmpty
            || canvasRect.IsEmpty
            || canvasWidth <= 0
            || canvasHeight <= 0)
        {
            return Int32Rect.Empty;
        }

        var left = Math.Clamp(selectionRect.Left, canvasRect.Left, canvasRect.Right);
        var top = Math.Clamp(selectionRect.Top, canvasRect.Top, canvasRect.Bottom);
        var right = Math.Clamp(selectionRect.Right, canvasRect.Left, canvasRect.Right);
        var bottom = Math.Clamp(selectionRect.Bottom, canvasRect.Top, canvasRect.Bottom);

        var normalizedLeft = Math.Min(left, right) - canvasRect.Left;
        var normalizedTop = Math.Min(top, bottom) - canvasRect.Top;
        var normalizedRight = Math.Max(left, right) - canvasRect.Left;
        var normalizedBottom = Math.Max(top, bottom) - canvasRect.Top;

        var pixelLeft = Math.Clamp((int)Math.Floor(normalizedLeft), 0, canvasWidth);
        var pixelTop = Math.Clamp((int)Math.Floor(normalizedTop), 0, canvasHeight);
        var pixelRight = Math.Clamp((int)Math.Ceiling(normalizedRight), 0, canvasWidth);
        var pixelBottom = Math.Clamp((int)Math.Ceiling(normalizedBottom), 0, canvasHeight);

        var width = Math.Max(0, pixelRight - pixelLeft);
        var height = Math.Max(0, pixelBottom - pixelTop);

        return width == 0 || height == 0
            ? Int32Rect.Empty
            : new Int32Rect(pixelLeft, pixelTop, width, height);
    }

    public static IReadOnlyList<AnimationFrame> OffsetFrames(IReadOnlyList<AnimationFrame> frames, int deltaX, int deltaY)
    {
        ArgumentNullException.ThrowIfNull(frames);

        var shiftedFrames = new AnimationFrame[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            shiftedFrames[i] = frames[i].WithPosition(frames[i].X + deltaX, frames[i].Y + deltaY);
        }

        return shiftedFrames;
    }
}

internal readonly record struct CanvasViewportLayout(double WorkspaceWidth, double WorkspaceHeight, Rect CanvasRect, Rect FrameRect)
{
    public static CanvasViewportLayout Empty { get; } = new(1, 1, Rect.Empty, Rect.Empty);
}
