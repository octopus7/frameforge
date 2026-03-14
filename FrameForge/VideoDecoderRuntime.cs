using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FFMediaToolkit;

namespace FrameForge;

public readonly record struct VideoRuntimeInstallProgress(string StatusText, long BytesReceived, long? TotalBytes);

public readonly record struct VideoRuntimeStatus(
    string RuntimeDirectory,
    bool IsCustomDirectory,
    bool IsReady,
    string StatusMessage,
    string[] MissingFiles);

internal static class VideoDecoderRuntime
{
    private const string DownloadFileName = "ffmpeg-n7.1-latest-win64-gpl-shared-7.1.zip";

    private static bool _isLoaded;
    private static string? _configuredRuntimeDirectory;
    private static string? _loadedRuntimeDirectory;

    private static readonly string[] RequiredLibraries =
    [
        "avcodec-61.dll",
        "avformat-61.dll",
        "avutil-59.dll",
        "swresample-5.dll",
        "swscale-8.dll"
    ];

    public static string DownloadSourceName => "BtbN/FFmpeg-Builds";

    public static string DownloadVersionLabel => "FFmpeg 7.1 shared build (win64 GPL)";

    public static string DownloadUrl =>
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n7.1-latest-win64-gpl-shared-7.1.zip";

    public static string DefaultRuntimeDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FrameForge",
            "ffmpeg",
            "7.1",
            "win-x64",
            "native");

    public static string RuntimeDirectory
    {
        get
        {
            var configuredPath = AppSettingsService.Current.VideoRuntimePath;
            return string.IsNullOrWhiteSpace(configuredPath)
                ? DefaultRuntimeDirectory
                : configuredPath.Trim();
        }
    }

    public static bool HasCustomRuntimeDirectory =>
        !string.IsNullOrWhiteSpace(AppSettingsService.Current.VideoRuntimePath);

    public static bool IsRestartRequired =>
        _isLoaded && !PathsEqual(RuntimeDirectory, _loadedRuntimeDirectory);

    public static string? LoadedRuntimeDirectory => _loadedRuntimeDirectory;

    public static void Configure()
    {
        EnsureConfigured(RuntimeDirectory);
    }

    public static VideoRuntimeStatus GetStatus()
    {
        var runtimeDirectory = RuntimeDirectory;
        var isCustomDirectory = HasCustomRuntimeDirectory;

        if (!Directory.Exists(runtimeDirectory))
        {
            return new VideoRuntimeStatus(
                runtimeDirectory,
                isCustomDirectory,
                false,
                "FFmpeg 런타임 폴더가 없습니다.",
                RequiredLibraries);
        }

        var missingFiles = RequiredLibraries
            .Where(fileName => !File.Exists(Path.Combine(runtimeDirectory, fileName)))
            .ToArray();

        if (missingFiles.Length > 0)
        {
            return new VideoRuntimeStatus(
                runtimeDirectory,
                isCustomDirectory,
                false,
                "필수 FFmpeg DLL이 일부 누락되어 있습니다.",
                missingFiles);
        }

        return new VideoRuntimeStatus(
            runtimeDirectory,
            isCustomDirectory,
            true,
            "FFmpeg 런타임을 사용할 수 있습니다.",
            []);
    }

    public static void SetRuntimeDirectoryOverride(string? runtimeDirectory)
    {
        AppSettingsService.SetVideoRuntimePath(runtimeDirectory);
        RefreshConfigurationState();
    }

    public static void ResetToDefaultRuntimeDirectory()
    {
        AppSettingsService.SetVideoRuntimePath(null);
        RefreshConfigurationState();
    }

    public static bool TryEnsureLoaded(out string? errorMessage)
    {
        if (_isLoaded)
        {
            errorMessage = null;
            return true;
        }

        var status = GetStatus();
        if (!status.IsReady)
        {
            errorMessage = $"{status.StatusMessage}\n{status.RuntimeDirectory}";
            if (status.MissingFiles.Length > 0)
            {
                errorMessage += $"\n누락 파일: {string.Join(", ", status.MissingFiles)}";
            }

            return false;
        }

        try
        {
            EnsureConfigured(status.RuntimeDirectory);
            FFmpegLoader.LoadFFmpeg();
            _isLoaded = true;
            _loadedRuntimeDirectory = NormalizeRuntimeDirectory(status.RuntimeDirectory);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"FFmpeg 라이브러리를 불러올 수 없습니다.\n{ex.Message}";
            return false;
        }
    }

    public static async Task InstallDefaultRuntimeAsync(
        IProgress<VideoRuntimeInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ResetToDefaultRuntimeDirectory();

        var runtimeDirectory = DefaultRuntimeDirectory;
        var downloadDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FrameForge",
            "downloads");
        var zipPath = Path.Combine(downloadDirectory, DownloadFileName);

        Directory.CreateDirectory(downloadDirectory);
        Directory.CreateDirectory(runtimeDirectory);

        progress?.Report(new VideoRuntimeInstallProgress("FFmpeg 패키지를 다운로드하는 중입니다...", 0, null));

        using (var client = new HttpClient())
        using (var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = File.Create(zipPath);

            var buffer = new byte[81920];
            long totalRead = 0;

            while (true)
            {
                var bytesRead = await responseStream.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;
                progress?.Report(new VideoRuntimeInstallProgress("FFmpeg 패키지를 다운로드하는 중입니다...", totalRead, totalBytes));
            }
        }

        progress?.Report(new VideoRuntimeInstallProgress("FFmpeg DLL을 설치하는 중입니다...", 0, null));

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            var dllEntries = archive.Entries
                .Where(entry => entry.FullName.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                    && entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var entry in dllEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationPath = Path.Combine(runtimeDirectory, entry.Name);
                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }

        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        RefreshConfigurationState();
    }

    private static void RefreshConfigurationState()
    {
        if (_isLoaded)
        {
            return;
        }

        _configuredRuntimeDirectory = null;
    }

    private static void EnsureConfigured(string runtimeDirectory)
    {
        if (_isLoaded)
        {
            return;
        }

        var normalizedRuntimeDirectory = NormalizeRuntimeDirectory(runtimeDirectory);
        if (!Directory.Exists(normalizedRuntimeDirectory))
        {
            return;
        }

        if (PathsEqual(_configuredRuntimeDirectory, normalizedRuntimeDirectory))
        {
            return;
        }

        FFmpegLoader.FFmpegPath = normalizedRuntimeDirectory;
        _configuredRuntimeDirectory = normalizedRuntimeDirectory;
    }

    private static string NormalizeRuntimeDirectory(string runtimeDirectory)
    {
        var trimmedPath = runtimeDirectory.Trim();
        var fullPath = Path.GetFullPath(trimmedPath);
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);
        }

        return string.Equals(
            NormalizeRuntimeDirectory(left),
            NormalizeRuntimeDirectory(right),
            StringComparison.OrdinalIgnoreCase);
    }
}
