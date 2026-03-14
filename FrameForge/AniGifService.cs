using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace FrameForge;

public sealed record AniGifSequenceAnalysis(
    string SamplePngPath,
    string DirectoryPath,
    string Prefix,
    int RangeStart,
    int RangeEnd,
    int NumberWidth,
    int FileCount);

public static class AniGifService
{
    public const int DefaultFrameDelayMilliseconds = 125;

    public static AniGifSequenceAnalysis AnalyzeSequence(string samplePngPath)
    {
        if (string.IsNullOrWhiteSpace(samplePngPath))
        {
            throw new ArgumentException("샘플 PNG 파일 경로가 필요합니다.", nameof(samplePngPath));
        }

        var normalizedSamplePath = Path.GetFullPath(samplePngPath);
        if (!File.Exists(normalizedSamplePath))
        {
            throw new FileNotFoundException("샘플 PNG 파일을 찾을 수 없습니다.", normalizedSamplePath);
        }

        if (!string.Equals(Path.GetExtension(normalizedSamplePath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PNG 파일만 선택할 수 있습니다.");
        }

        if (!TryParseNumberedPngFileName(normalizedSamplePath, out var prefix, out _, out var numberWidth))
        {
            throw new InvalidOperationException("선택한 PNG 파일명에 연속된 번호가 포함되어야 합니다.");
        }

        var directoryPath = Path.GetDirectoryName(normalizedSamplePath)
            ?? throw new InvalidOperationException("PNG 폴더를 확인할 수 없습니다.");
        var matchedFiles = GetMatchedPngFiles(directoryPath, prefix);
        if (matchedFiles.Count == 0)
        {
            throw new InvalidOperationException("같은 프리픽스의 PNG 시퀀스를 찾을 수 없습니다.");
        }

        var maxNumberWidth = Math.Max(numberWidth, matchedFiles.Values.Max(item => item.NumberWidth));
        return new AniGifSequenceAnalysis(
            normalizedSamplePath,
            directoryPath,
            prefix,
            matchedFiles.Keys.Min(),
            matchedFiles.Keys.Max(),
            maxNumberWidth,
            matchedFiles.Count);
    }

    public static IReadOnlyList<string> ResolveFramePaths(string samplePngPath, string prefix, int rangeStart, int rangeEnd)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("프리픽스가 필요합니다.", nameof(prefix));
        }

        if (rangeStart <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rangeStart), "시작 번호는 1 이상이어야 합니다.");
        }

        if (rangeEnd < rangeStart)
        {
            throw new ArgumentOutOfRangeException(nameof(rangeEnd), "끝 번호는 시작 번호보다 크거나 같아야 합니다.");
        }

        var analysis = AnalyzeSequence(samplePngPath);
        var matchedFiles = GetMatchedPngFiles(analysis.DirectoryPath, prefix.Trim());
        if (matchedFiles.Count == 0)
        {
            throw new InvalidOperationException("입력한 프리픽스와 일치하는 PNG 파일을 찾을 수 없습니다.");
        }

        var resolvedPaths = new List<string>(rangeEnd - rangeStart + 1);
        for (var number = rangeStart; number <= rangeEnd; number++)
        {
            if (matchedFiles.TryGetValue(number, out var matchedFile))
            {
                resolvedPaths.Add(matchedFile.Path);
            }
        }

        if (resolvedPaths.Count == 0)
        {
            throw new InvalidOperationException(
                $"지정한 범위 {rangeStart} ~ {rangeEnd} 안에 사용할 PNG 파일이 없습니다.");
        }

        return resolvedPaths;
    }

    public static void CreateAnimatedGif(
        string outputPath,
        IReadOnlyList<string> pngPaths,
        int frameDelayMilliseconds = DefaultFrameDelayMilliseconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(pngPaths);

        if (pngPaths.Count == 0)
        {
            throw new InvalidOperationException("GIF로 만들 PNG 프레임이 없습니다.");
        }

        if (frameDelayMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameDelayMilliseconds),
                "프레임 지연시간은 1ms 이상이어야 합니다.");
        }

        var normalizedOutputPath = Path.GetFullPath(outputPath);
        var outputDirectory = Path.GetDirectoryName(normalizedOutputPath)
            ?? throw new InvalidOperationException("출력 폴더를 확인할 수 없습니다.");
        Directory.CreateDirectory(outputDirectory);

        var delayInHundredths = Math.Max(1, (int)Math.Round(frameDelayMilliseconds / 10d, MidpointRounding.AwayFromZero));
        var encoder = new GifEncoder();

        try
        {
            using var animation = LoadFrame(pngPaths[0]);
            ConfigureGifFrame(animation.Frames.RootFrame, delayInHundredths);
            animation.Metadata.GetGifMetadata().RepeatCount = 0;

            for (var i = 1; i < pngPaths.Count; i++)
            {
                using var sourceFrame = LoadFrame(pngPaths[i]);
                EnsureMatchingSize(sourceFrame, animation.Width, animation.Height);

                var addedFrame = animation.Frames.AddFrame(sourceFrame.Frames.RootFrame);
                ConfigureGifFrame(addedFrame, delayInHundredths);
            }

            animation.SaveAsGif(normalizedOutputPath, encoder);
        }
        catch
        {
            if (File.Exists(normalizedOutputPath))
            {
                File.Delete(normalizedOutputPath);
            }

            throw;
        }
    }

    public static string BuildDefaultOutputFileName(string prefix, int rangeStart, int rangeEnd, int numberWidth)
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? "anigif" : prefix.Trim();
        var digitWidth = Math.Max(1, numberWidth);
        var startText = rangeStart.ToString($"D{digitWidth}", CultureInfo.InvariantCulture);
        var endText = rangeEnd.ToString($"D{digitWidth}", CultureInfo.InvariantCulture);
        return $"{normalizedPrefix}{startText}-{endText}.gif";
    }

    private static Image<Rgba32> LoadFrame(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("PNG 파일을 찾을 수 없습니다.", path);
        }

        return Image.Load<Rgba32>(path);
    }

    private static Dictionary<int, (string Path, int NumberWidth)> GetMatchedPngFiles(string directoryPath, string prefix)
    {
        var matchedFiles = new Dictionary<int, (string Path, int NumberWidth)>();

        foreach (var pngPath in Directory.EnumerateFiles(directoryPath, "*.png"))
        {
            if (!TryParseNumberedPngFileName(pngPath, out var detectedPrefix, out var number, out var numberWidth))
            {
                continue;
            }

            if (!string.Equals(detectedPrefix, prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matchedFiles[number] = (pngPath, numberWidth);
        }

        return matchedFiles;
    }

    private static bool TryParseNumberedPngFileName(
        string pngPath,
        out string prefix,
        out int number,
        out int numberWidth)
    {
        prefix = string.Empty;
        number = 0;
        numberWidth = 0;

        var stem = Path.GetFileNameWithoutExtension(pngPath);
        if (string.IsNullOrWhiteSpace(stem))
        {
            return false;
        }

        var digitStartIndex = stem.Length;
        while (digitStartIndex > 0 && char.IsDigit(stem[digitStartIndex - 1]))
        {
            digitStartIndex--;
        }

        if (digitStartIndex == stem.Length)
        {
            return false;
        }

        prefix = stem[..digitStartIndex];
        var numberText = stem[digitStartIndex..];
        if (!int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out number))
        {
            return false;
        }

        numberWidth = numberText.Length;
        return true;
    }

    private static void EnsureMatchingSize(Image image, int expectedWidth, int expectedHeight)
    {
        if (image.Width != expectedWidth || image.Height != expectedHeight)
        {
            throw new InvalidOperationException("모든 PNG 프레임은 같은 크기여야 합니다.");
        }
    }

    private static void ConfigureGifFrame(ImageFrame<Rgba32> frame, int delayInHundredths)
    {
        var gifMetadata = frame.Metadata.GetGifMetadata();
        gifMetadata.FrameDelay = delayInHundredths;
        gifMetadata.DisposalMethod = GifDisposalMethod.RestoreToBackground;
    }
}
