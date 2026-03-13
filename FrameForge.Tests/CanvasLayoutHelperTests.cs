using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace FrameForge.Tests;

public sealed class CanvasLayoutHelperTests
{
    [Fact]
    public void CenterFrame_UsesFloorForOddOffsets()
    {
        var position = CanvasLayoutHelper.CenterFrame(5, 7, 2, 2);

        Assert.Equal(1, position.X);
        Assert.Equal(2, position.Y);
    }

    [Fact]
    public void ClampSelectionToCanvas_ClampsAndRoundsToPixels()
    {
        var selection = new Rect(12.2, 21.4, 6.3, 5.1);
        var canvasRect = new Rect(10, 20, 16, 12);

        var pixelRect = CanvasLayoutHelper.ClampSelectionToCanvas(selection, canvasRect, 16, 12);

        Assert.Equal(new Int32Rect(2, 1, 7, 6), pixelRect);
    }

    [Fact]
    public void CalculateViewport_AnchorsCanvasAndOffsetsFrame()
    {
        var frame = new AnimationFrame("frame", CreateBitmap(20, 10), -3, 4);

        var viewport = CanvasLayoutHelper.CalculateViewport(16, 12, frame);

        Assert.Equal(56, viewport.WorkspaceWidth);
        Assert.Equal(52, viewport.WorkspaceHeight);
        Assert.Equal(new Rect(20, 20, 16, 12), viewport.CanvasRect);
        Assert.Equal(new Rect(17, 24, 20, 10), viewport.FrameRect);
    }

    [Fact]
    public void OffsetFrames_ShiftsAllPositions()
    {
        var bitmap = CreateBitmap(2, 2);
        var frames = new[]
        {
            new AnimationFrame("a", bitmap, 0, 0),
            new AnimationFrame("b", bitmap, -3, 4)
        };

        var shifted = CanvasLayoutHelper.OffsetFrames(frames, -2, 5);

        Assert.Collection(
            shifted,
            frame =>
            {
                Assert.Equal(-2, frame.X);
                Assert.Equal(5, frame.Y);
            },
            frame =>
            {
                Assert.Equal(-5, frame.X);
                Assert.Equal(9, frame.Y);
            });
    }

    [Fact]
    public void SaveAndLoadProject_PreservesCanvasAndFramePositions()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var projectPath = Path.Combine(tempRoot, "sample.ffproj");
            var frames = new[]
            {
                new AnimationFrame("first", CreateBitmap(4, 4, Colors.Red), 0, 0),
                new AnimationFrame("second", CreateBitmap(2, 6, Colors.Blue), -1, 3)
            };

            ProjectStorageService.SaveProject(projectPath, frames, 4, 4, isLoopEnabled: true, selectedFrameIndex: 1);
            var loadResult = ProjectStorageService.LoadProject(projectPath);

            Assert.Equal(4, loadResult.CanvasWidth);
            Assert.Equal(4, loadResult.CanvasHeight);
            Assert.True(loadResult.IsLoopEnabled);
            Assert.Equal(1, loadResult.SelectedFrameIndex);
            Assert.Equal(0, loadResult.MissingFrameCount);
            Assert.Collection(
                loadResult.Frames,
                frame =>
                {
                    Assert.Equal("first", frame.Name);
                    Assert.Equal(0, frame.X);
                    Assert.Equal(0, frame.Y);
                    Assert.Equal(4, frame.Image.PixelWidth);
                    Assert.Equal(4, frame.Image.PixelHeight);
                },
                frame =>
                {
                    Assert.Equal("second", frame.Name);
                    Assert.Equal(-1, frame.X);
                    Assert.Equal(3, frame.Y);
                    Assert.Equal(2, frame.Image.PixelWidth);
                    Assert.Equal(6, frame.Image.PixelHeight);
                });
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void LoadProject_Version1_AssignsCanvasAndCentersLaterFrames()
    {
        var tempRoot = CreateTempDirectory();

        try
        {
            var first = CreateBitmap(4, 4, Colors.Red);
            var second = CreateBitmap(2, 2, Colors.Green);
            var firstAsset = WriteAsset(tempRoot, "legacy", first);
            var secondAsset = WriteAsset(tempRoot, "legacy", second);

            var document = new FrameProjectDocument
            {
                Version = 1,
                App = "FrameForge",
                SavedAtUtc = DateTime.UtcNow,
                AssetsRoot = "legacy.assets/frames",
                Settings = new FrameProjectSettings
                {
                    IsLoopEnabled = false,
                    SelectedFrameIndex = 1
                },
                Frames =
                [
                    new FrameProjectFrameEntry
                    {
                        Name = "first",
                        AssetId = firstAsset.AssetId,
                        AssetPath = firstAsset.AssetPath
                    },
                    new FrameProjectFrameEntry
                    {
                        Name = "second",
                        AssetId = secondAsset.AssetId,
                        AssetPath = secondAsset.AssetPath
                    }
                ]
            };

            var projectPath = Path.Combine(tempRoot, "legacy.ffproj");
            File.WriteAllText(projectPath, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));

            var loadResult = ProjectStorageService.LoadProject(projectPath);

            Assert.Equal(4, loadResult.CanvasWidth);
            Assert.Equal(4, loadResult.CanvasHeight);
            Assert.Equal(1, loadResult.SelectedFrameIndex);
            Assert.Collection(
                loadResult.Frames,
                frame =>
                {
                    Assert.Equal(0, frame.X);
                    Assert.Equal(0, frame.Y);
                },
                frame =>
                {
                    Assert.Equal(1, frame.X);
                    Assert.Equal(1, frame.Y);
                });
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static BitmapSource CreateBitmap(int width, int height, Color? color = null)
    {
        var fillColor = color ?? Colors.White;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = fillColor.B;
            pixels[i + 1] = fillColor.G;
            pixels[i + 2] = fillColor.R;
            pixels[i + 3] = fillColor.A == 0 ? (byte)255 : fillColor.A;
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

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "FrameForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static (string AssetId, string AssetPath) WriteAsset(string root, string projectStem, BitmapSource image)
    {
        var pngBytes = ProjectStorageService.EncodePng(image);
        var assetId = ProjectStorageService.ComputeSha256(pngBytes);
        var assetPath = $"{projectStem}.assets/frames/{assetId[..2]}/{assetId.Substring(2, 2)}/{assetId}.png";
        var fullPath = Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, pngBytes);
        return (assetId, assetPath);
    }
}
