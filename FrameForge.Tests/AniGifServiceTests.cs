using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace FrameForge.Tests;

public sealed class AniGifServiceTests
{
    [Fact]
    public void AnalyzeSequence_DetectsPrefixAndRangeFromSamplePng()
    {
        var workingDirectory = CreateTempDirectory();

        try
        {
            WritePng(Path.Combine(workingDirectory, "walk_0003.png"), 8, 8);
            WritePng(Path.Combine(workingDirectory, "walk_0004.png"), 8, 8);
            WritePng(Path.Combine(workingDirectory, "walk_0005.png"), 8, 8);
            WritePng(Path.Combine(workingDirectory, "idle_0001.png"), 8, 8);

            var analysis = AniGifService.AnalyzeSequence(Path.Combine(workingDirectory, "walk_0004.png"));

            Assert.Equal(Path.Combine(workingDirectory, "walk_0004.png"), analysis.SamplePngPath);
            Assert.Equal(workingDirectory, analysis.DirectoryPath);
            Assert.Equal("walk_", analysis.Prefix);
            Assert.Equal(3, analysis.RangeStart);
            Assert.Equal(5, analysis.RangeEnd);
            Assert.Equal(4, analysis.NumberWidth);
            Assert.Equal(3, analysis.FileCount);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResolveFramePaths_ReturnsRequestedRangeInSequenceOrder()
    {
        var workingDirectory = CreateTempDirectory();

        try
        {
            WritePng(Path.Combine(workingDirectory, "fx_0010.png"), 8, 8);
            WritePng(Path.Combine(workingDirectory, "fx_0011.png"), 8, 8);
            WritePng(Path.Combine(workingDirectory, "fx_0012.png"), 8, 8);

            var resolvedPaths = AniGifService.ResolveFramePaths(
                Path.Combine(workingDirectory, "fx_0011.png"),
                "fx_",
                10,
                12);
            var resolvedFileNames = resolvedPaths.Select(path => Path.GetFileName(path)!).ToArray();

            Assert.Equal(
                ["fx_0010.png", "fx_0011.png", "fx_0012.png"],
                resolvedFileNames);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResolveFramePaths_SkipsMissingFramesInsideRequestedRange()
    {
        var workingDirectory = CreateTempDirectory();

        try
        {
            WritePng(Path.Combine(workingDirectory, "fx_0001.png"), 8, 8);
            WritePng(Path.Combine(workingDirectory, "fx_0003.png"), 8, 8);
            WritePng(Path.Combine(workingDirectory, "fx_0004.png"), 8, 8);
            WritePng(Path.Combine(workingDirectory, "fx_0006.png"), 8, 8);

            var resolvedPaths = AniGifService.ResolveFramePaths(
                Path.Combine(workingDirectory, "fx_0003.png"),
                "fx_",
                1,
                6);
            var resolvedFileNames = resolvedPaths.Select(path => Path.GetFileName(path)!).ToArray();

            Assert.Equal(
                ["fx_0001.png", "fx_0003.png", "fx_0004.png", "fx_0006.png"],
                resolvedFileNames);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreateAnimatedGif_WritesAnimatedGifWithExpectedFrameDelay()
    {
        var workingDirectory = CreateTempDirectory();

        try
        {
            var pngPaths = new[]
            {
                Path.Combine(workingDirectory, "loop_0001.png"),
                Path.Combine(workingDirectory, "loop_0002.png"),
                Path.Combine(workingDirectory, "loop_0003.png")
            };

            WritePng(pngPaths[0], 8, 8);
            WritePng(pngPaths[1], 8, 8);
            WritePng(pngPaths[2], 8, 8);

            var outputPath = Path.Combine(workingDirectory, "loop.gif");
            AniGifService.CreateAnimatedGif(outputPath, pngPaths);

            Assert.True(File.Exists(outputPath));

            var decoder = new GifBitmapDecoder(
                new Uri(outputPath),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            Assert.Equal(3, decoder.Frames.Count);

            var metadata = Assert.IsType<BitmapMetadata>(decoder.Frames[0].Metadata);
            var delay = Assert.IsType<ushort>(metadata.GetQuery("/grctlext/Delay"));
            Assert.Equal((ushort)13, delay);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"frameforge-anigif-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        return workingDirectory;
    }

    private static void WritePng(string path, int width, int height)
    {
        File.WriteAllBytes(path, ProjectStorageService.EncodePng(CreateBitmap(width, height)));
    }

    private static BitmapSource CreateBitmap(int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];

        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = Colors.White.B;
            pixels[i + 1] = Colors.White.G;
            pixels[i + 2] = Colors.White.R;
            pixels[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }
}
