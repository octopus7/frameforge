using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;

namespace FrameForge;

public readonly record struct VideoFrameCaptureProgress(long DecodedFrameCount, long? TotalFrameCount);

public sealed class VideoCapturedFrame
{
    public VideoCapturedFrame(int sourceIndex, TimeSpan timestamp, BitmapSource image)
    {
        SourceIndex = sourceIndex;
        Timestamp = timestamp;
        Image = image;
    }

    public int SourceIndex { get; }
    public TimeSpan Timestamp { get; }
    public BitmapSource Image { get; }
}

public static class VideoFrameCaptureService
{
    public static IReadOnlyList<VideoCapturedFrame> CaptureFrames(
        string videoPath,
        IProgress<VideoFrameCaptureProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(videoPath);

        using var media = MediaFile.Open(
            videoPath,
            new MediaOptions
            {
                StreamsToLoad = MediaMode.Video,
                VideoPixelFormat = ImagePixelFormat.Bgr24
            });

        if (!media.HasVideo)
        {
            throw new InvalidOperationException("선택한 파일에 비디오 스트림이 없습니다.");
        }

        var frameSize = media.Video.Info.FrameSize;
        var decodedFrames = new List<VideoCapturedFrame>();
        var totalFrameCount = media.Video.Info.NumberOfFrames;
        var bitmap = new WriteableBitmap(frameSize.Width, frameSize.Height, 96, 96, PixelFormats.Bgr24, null);
        var copyStride = bitmap.BackBufferStride;
        var dirtyRect = new Int32Rect(0, 0, frameSize.Width, frameSize.Height);

        for (var sourceIndex = 0; ; sourceIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hasFrame = false;
            bitmap.Lock();
            try
            {
                hasFrame = media.Video.TryGetNextFrame(bitmap.BackBuffer, bitmap.BackBufferStride);
                if (hasFrame)
                {
                    bitmap.AddDirtyRect(dirtyRect);
                }
            }
            finally
            {
                bitmap.Unlock();
            }

            if (!hasFrame)
            {
                break;
            }

            var pixels = new byte[copyStride * frameSize.Height];
            bitmap.CopyPixels(pixels, copyStride, 0);

            var frame = BitmapSource.Create(
                frameSize.Width,
                frameSize.Height,
                96,
                96,
                PixelFormats.Bgr24,
                null,
                pixels,
                copyStride);
            frame.Freeze();

            var timestamp = media.Video.Position;
            if (timestamp < TimeSpan.Zero)
            {
                timestamp = TimeSpan.Zero;
            }

            decodedFrames.Add(new VideoCapturedFrame(sourceIndex, timestamp, frame));
            progress?.Report(new VideoFrameCaptureProgress(decodedFrames.Count, totalFrameCount));
        }

        return decodedFrames;
    }
}
