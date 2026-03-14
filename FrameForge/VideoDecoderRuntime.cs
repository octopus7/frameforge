using System;
using System.IO;
using System.Linq;
using FFMediaToolkit;

namespace FrameForge;

internal static class VideoDecoderRuntime
{
    private static bool _isLoaded;

    private static readonly string[] RequiredLibraries =
    [
        "avcodec-61.dll",
        "avformat-61.dll",
        "avutil-59.dll",
        "swresample-5.dll",
        "swscale-8.dll"
    ];

    public static string RuntimeDirectory =>
        Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native");

    public static void Configure()
    {
        FFmpegLoader.FFmpegPath = RuntimeDirectory;
    }

    public static bool TryEnsureLoaded(out string? errorMessage)
    {
        Configure();

        if (_isLoaded)
        {
            errorMessage = null;
            return true;
        }

        if (!Directory.Exists(RuntimeDirectory))
        {
            errorMessage = $"FFmpeg 런타임 폴더를 찾을 수 없습니다.\n{RuntimeDirectory}";
            return false;
        }

        var missingFiles = RequiredLibraries
            .Where(fileName => !File.Exists(Path.Combine(RuntimeDirectory, fileName)))
            .ToArray();
        if (missingFiles.Length > 0)
        {
            errorMessage = $"FFmpeg 라이브러리가 누락되었습니다.\n{string.Join(", ", missingFiles)}";
            return false;
        }

        try
        {
            FFmpegLoader.LoadFFmpeg();
            _isLoaded = true;
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"FFmpeg 라이브러리를 불러올 수 없습니다.\n{ex.Message}";
            return false;
        }
    }
}
