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
    private const int CurrentProjectVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static void SaveProject(string projectPath, IReadOnlyList<AnimationFrame> frames, bool isLoopEnabled, int selectedFrameIndex)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentException("프로젝트 경로가 비어 있습니다.", nameof(projectPath));
        }

        ArgumentNullException.ThrowIfNull(frames);

        var normalizedProjectPath = Path.GetFullPath(projectPath);
        var projectDirectory = Path.GetDirectoryName(normalizedProjectPath)
            ?? throw new InvalidOperationException("프로젝트 디렉터리를 확인할 수 없습니다.");
        var projectStem = Path.GetFileNameWithoutExtension(normalizedProjectPath);
        if (string.IsNullOrWhiteSpace(projectStem))
        {
            throw new InvalidOperationException("프로젝트 파일 이름이 올바르지 않습니다.");
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
                AssetPath = assetRelativePath
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
                SelectedFrameIndex = normalizedSelectedIndex
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
            throw new ArgumentException("프로젝트 경로가 비어 있습니다.", nameof(projectPath));
        }

        var normalizedProjectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(normalizedProjectPath))
        {
            throw new FileNotFoundException("프로젝트 파일을 찾을 수 없습니다.", normalizedProjectPath);
        }

        var json = File.ReadAllText(normalizedProjectPath, Encoding.UTF8);
        var document = JsonSerializer.Deserialize<FrameProjectDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("프로젝트 파일을 읽을 수 없습니다.");

        if (document.Version != CurrentProjectVersion)
        {
            throw new NotSupportedException($"지원하지 않는 프로젝트 버전입니다. (version={document.Version})");
        }

        var projectDirectory = Path.GetDirectoryName(normalizedProjectPath)
            ?? throw new InvalidOperationException("프로젝트 디렉터리를 확인할 수 없습니다.");
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
                loadedFrames.Add(new AnimationFrame(frameName, image, assetFullPath, entry.AssetId, entry.AssetPath));
            }
            catch
            {
                missingFrameCount++;
            }
        }

        var loadedSelectedIndex = loadedFrames.Count == 0
            ? -1
            : Math.Clamp(document.Settings?.SelectedFrameIndex ?? 0, 0, loadedFrames.Count - 1);

        return new FrameProjectLoadResult(
            loadedFrames,
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
            throw new InvalidOperationException("저장 디렉터리를 확인할 수 없습니다.");
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
