using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace FrameForge;

public static class ProjectStorageService
{
    private const int CurrentProjectVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static void SaveProject(
        string projectPath,
        IReadOnlyList<AnimationFrame> frames,
        int canvasWidth,
        int canvasHeight,
        bool isLoopEnabled,
        int selectedFrameIndex)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentException("Project path is required.", nameof(projectPath));
        }

        ArgumentNullException.ThrowIfNull(frames);

        var normalizedProjectPath = Path.GetFullPath(projectPath);
        var projectDirectory = Path.GetDirectoryName(normalizedProjectPath)
            ?? throw new InvalidOperationException("Could not determine the project directory.");
        var projectStem = Path.GetFileNameWithoutExtension(normalizedProjectPath);
        if (string.IsNullOrWhiteSpace(projectStem))
        {
            throw new InvalidOperationException("Could not determine the project file name.");
        }

        var assetsRootRelative = $"{projectStem}.assets/frames";
        var assetsRootAbsolute = Path.Combine(projectDirectory, $"{projectStem}.assets", "frames");
        Directory.CreateDirectory(assetsRootAbsolute);

        var frameEntries = new List<FrameProjectFrameEntry>(frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var pngBytes = EncodePng(frame.Image);
            var assetId = ComputeSha256(pngBytes);

            var shardA = assetId[..2];
            var shardB = assetId.Substring(2, 2);
            var assetRelativePath = $"{assetsRootRelative}/{shardA}/{shardB}/{assetId}.png";
            var assetAbsolutePath = Path.Combine(projectDirectory, $"{projectStem}.assets", "frames", shardA, shardB, $"{assetId}.png");

            if (!File.Exists(assetAbsolutePath))
            {
                WriteBytesAtomic(assetAbsolutePath, pngBytes);
            }

            var frameName = string.IsNullOrWhiteSpace(frame.Name) ? $"Frame_{i + 1:000}" : frame.Name;
            frameEntries.Add(new FrameProjectFrameEntry
            {
                Name = frameName,
                AssetId = assetId,
                AssetPath = assetRelativePath,
                X = frame.X,
                Y = frame.Y
            });
        }

        var normalizedSelectedIndex = frames.Count == 0
            ? -1
            : Math.Clamp(selectedFrameIndex, 0, frames.Count - 1);

        var document = new FrameProjectDocument
        {
            Version = CurrentProjectVersion,
            App = "FrameForge",
            SavedAtUtc = DateTime.UtcNow,
            AssetsRoot = assetsRootRelative,
            Settings = new FrameProjectSettings
            {
                IsLoopEnabled = isLoopEnabled,
                SelectedFrameIndex = normalizedSelectedIndex,
                CanvasWidth = Math.Max(0, canvasWidth),
                CanvasHeight = Math.Max(0, canvasHeight)
            },
            Frames = frameEntries
        };

        var json = JsonSerializer.Serialize(document, JsonOptions);
        WriteTextAtomic(normalizedProjectPath, json);
    }

    public static FrameProjectLoadResult LoadProject(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentException("Project path is required.", nameof(projectPath));
        }

        var normalizedProjectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(normalizedProjectPath))
        {
            throw new FileNotFoundException("Project file was not found.", normalizedProjectPath);
        }

        var json = File.ReadAllText(normalizedProjectPath, Encoding.UTF8);
        var document = JsonSerializer.Deserialize<FrameProjectDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("Project file could not be read.");

        if (document.Version is not 1 and not CurrentProjectVersion)
        {
            throw new NotSupportedException($"Unsupported project version: {document.Version}");
        }

        var projectDirectory = Path.GetDirectoryName(normalizedProjectPath)
            ?? throw new InvalidOperationException("Could not determine the project directory.");
        var projectDirectoryFullPath = Path.GetFullPath(projectDirectory);
        var projectDirectoryWithSeparator = Path.EndsInDirectorySeparator(projectDirectoryFullPath)
            ? projectDirectoryFullPath
            : projectDirectoryFullPath + Path.DirectorySeparatorChar;

        var loadedFrames = new List<AnimationFrame>();
        var missingFrameCount = 0;

        foreach (var entry in document.Frames ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.AssetPath))
            {
                missingFrameCount++;
                continue;
            }

            var assetFullPath = ResolveAssetFullPath(projectDirectory, entry.AssetPath);
            if (!assetFullPath.StartsWith(projectDirectoryWithSeparator, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(assetFullPath))
            {
                missingFrameCount++;
                continue;
            }

            try
            {
                var image = LoadBitmap(assetFullPath);
                var frameName = string.IsNullOrWhiteSpace(entry.Name)
                    ? $"Frame_{loadedFrames.Count + 1:000}"
                    : entry.Name;
                loadedFrames.Add(new AnimationFrame(frameName, image, entry.X, entry.Y, assetFullPath, entry.AssetId, entry.AssetPath));
            }
            catch
            {
                missingFrameCount++;
            }
        }

        var canvasWidth = Math.Max(0, document.Settings?.CanvasWidth ?? 0);
        var canvasHeight = Math.Max(0, document.Settings?.CanvasHeight ?? 0);

        if (document.Version == 1)
        {
            (loadedFrames, canvasWidth, canvasHeight) = UpgradeVersion1Frames(loadedFrames);
        }
        else if ((canvasWidth <= 0 || canvasHeight <= 0) && loadedFrames.Count > 0)
        {
            canvasWidth = loadedFrames[0].Image.PixelWidth;
            canvasHeight = loadedFrames[0].Image.PixelHeight;
        }

        var loadedSelectedIndex = loadedFrames.Count == 0
            ? -1
            : Math.Clamp(document.Settings?.SelectedFrameIndex ?? 0, 0, loadedFrames.Count - 1);

        return new FrameProjectLoadResult(
            loadedFrames,
            canvasWidth,
            canvasHeight,
            document.Settings?.IsLoopEnabled ?? false,
            loadedSelectedIndex,
            missingFrameCount);
    }

    public static byte[] EncodePng(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static string ComputeSha256(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static string ResolveAssetFullPath(string projectDirectory, string assetPath)
    {
        var normalized = assetPath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(projectDirectory, normalized));
    }

    private static (List<AnimationFrame> Frames, int CanvasWidth, int CanvasHeight) UpgradeVersion1Frames(List<AnimationFrame> loadedFrames)
    {
        if (loadedFrames.Count == 0)
        {
            return (loadedFrames, 0, 0);
        }

        var canvasWidth = loadedFrames[0].Image.PixelWidth;
        var canvasHeight = loadedFrames[0].Image.PixelHeight;
        var upgradedFrames = new List<AnimationFrame>(loadedFrames.Count);

        for (var i = 0; i < loadedFrames.Count; i++)
        {
            var frame = loadedFrames[i];
            if (i == 0)
            {
                upgradedFrames.Add(frame.WithPosition(0, 0));
                continue;
            }

            var position = CanvasLayoutHelper.CenterFrame(
                canvasWidth,
                canvasHeight,
                frame.Image.PixelWidth,
                frame.Image.PixelHeight);
            upgradedFrames.Add(frame.WithPosition((int)position.X, (int)position.Y));
        }

        return (upgradedFrames, canvasWidth, canvasHeight);
    }

    private static void WriteTextAtomic(string targetPath, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        WriteBytesAtomic(targetPath, bytes);
    }

    private static void WriteBytesAtomic(string targetPath, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Could not determine the target directory.");
        }

        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
