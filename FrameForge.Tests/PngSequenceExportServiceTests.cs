using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace FrameForge.Tests;

public sealed class PngSequenceExportServiceTests
{
    [Fact]
    public void GetTargetPaths_UsesRenumberedSequentialFileNames()
    {
        var targetPaths = PngSequenceExportService.GetTargetPaths(@"C:\temp\exports", "walk", 12);

        Assert.Equal(12, targetPaths.Count);
        Assert.Equal("walk_0001.png", Path.GetFileName(targetPaths[0]));
        Assert.Equal("walk_0012.png", Path.GetFileName(targetPaths[^1]));
    }

    [Fact]
    public void Export_WritesPngFilesInFrameOrder()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"frameforge-export-{Guid.NewGuid():N}");

        try
        {
            var frames = Enumerable.Range(1, 3)
                .Select(index => new AnimationFrame($"Frame_{index:000}", CreateBitmap(index + 2, index + 3), 0, 0))
                .ToArray();

            var result = PngSequenceExportService.Export(outputDirectory, "hero", frames);

            Assert.Equal(outputDirectory, result.OutputDirectory);
            Assert.Equal("hero", result.FilePrefix);
            Assert.Equal(3, result.FileCount);
            var exportedFileNames = Directory.GetFiles(outputDirectory, "*.png")
                .Select(path => Path.GetFileName(path)!)
                .OrderBy(name => name)
                .ToArray();

            Assert.Equal(
                ["hero_0001.png", "hero_0002.png", "hero_0003.png"],
                exportedFileNames);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void SanitizePrefix_ReplacesInvalidFileNameCharacters()
    {
        var sanitizedPrefix = PngSequenceExportService.SanitizePrefix("walk:/hero?");

        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), sanitizedPrefix);
        Assert.Contains("walk", sanitizedPrefix);
        Assert.Contains("hero", sanitizedPrefix);
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
