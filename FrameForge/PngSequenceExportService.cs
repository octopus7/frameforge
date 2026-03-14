using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FrameForge;

public readonly record struct PngSequenceExportResult(
    string OutputDirectory,
    string FilePrefix,
    int FileCount,
    int NumberWidth);

public static class PngSequenceExportService
{
    public static PngSequenceExportResult Export(
        string outputDirectory,
        string filePrefix,
        IReadOnlyList<AnimationFrame> frames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePrefix);
        ArgumentNullException.ThrowIfNull(frames);

        if (frames.Count == 0)
        {
            throw new InvalidOperationException("내보낼 프레임이 없습니다.");
        }

        if (!IsValidPrefix(filePrefix))
        {
            throw new ArgumentException("프리픽스에 사용할 수 없는 문자가 있습니다.", nameof(filePrefix));
        }

        var normalizedOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(normalizedOutputDirectory);

        var targetPaths = GetTargetPaths(normalizedOutputDirectory, filePrefix, frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            var pngBytes = ProjectStorageService.EncodePng(frames[i].Image);
            File.WriteAllBytes(targetPaths[i], pngBytes);
        }

        return new PngSequenceExportResult(
            normalizedOutputDirectory,
            filePrefix.Trim(),
            frames.Count,
            GetNumberWidth(frames.Count));
    }

    public static List<string> GetTargetPaths(string outputDirectory, string filePrefix, int frameCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePrefix);

        if (frameCount <= 0)
        {
            return [];
        }

        var normalizedOutputDirectory = Path.GetFullPath(outputDirectory);
        var normalizedPrefix = filePrefix.Trim();
        var numberWidth = GetNumberWidth(frameCount);
        var targetPaths = new List<string>(frameCount);

        for (var i = 1; i <= frameCount; i++)
        {
            var fileName = $"{normalizedPrefix}_{i.ToString($"D{numberWidth}", CultureInfo.InvariantCulture)}.png";
            targetPaths.Add(Path.Combine(normalizedOutputDirectory, fileName));
        }

        return targetPaths;
    }

    public static bool IsValidPrefix(string? filePrefix)
    {
        if (string.IsNullOrWhiteSpace(filePrefix))
        {
            return false;
        }

        return filePrefix.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    public static string SanitizePrefix(string? filePrefix)
    {
        if (string.IsNullOrWhiteSpace(filePrefix))
        {
            return "frame";
        }

        var sanitizedPrefix = filePrefix.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            sanitizedPrefix = sanitizedPrefix.Replace(invalidChar, '_');
        }

        return string.IsNullOrWhiteSpace(sanitizedPrefix) ? "frame" : sanitizedPrefix;
    }

    private static int GetNumberWidth(int frameCount)
    {
        return Math.Max(4, frameCount.ToString(CultureInfo.InvariantCulture).Length);
    }
}
