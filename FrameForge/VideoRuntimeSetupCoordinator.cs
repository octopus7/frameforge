using System;
using System.Threading.Tasks;
using System.Windows;

namespace FrameForge;

internal static class VideoRuntimeSetupCoordinator
{
    public static async Task<bool> EnsureRuntimeAvailableAsync(Window owner)
    {
        if (VideoDecoderRuntime.TryEnsureLoaded(out _))
        {
            return true;
        }

        var status = VideoDecoderRuntime.GetStatus();
        var message =
            "동영상 임포트에는 FFmpeg 런타임이 필요합니다.\n\n" +
            $"출처: {VideoDecoderRuntime.DownloadSourceName}\n" +
            $"버전: {VideoDecoderRuntime.DownloadVersionLabel}\n" +
            $"현재 경로: {status.RuntimeDirectory}\n" +
            $"기본 설치 경로: {VideoDecoderRuntime.DefaultRuntimeDirectory}\n\n" +
            "예: 기본 경로(%LocalAppData%)에 자동 설치\n" +
            "아니오: 옵션 창의 FFmpeg 탭 열기\n" +
            "취소: 동영상 임포트 중단";

        var result = MessageBox.Show(
            owner,
            message,
            "FFmpeg 런타임 필요",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Information,
            MessageBoxResult.Yes);

        if (result == MessageBoxResult.Yes)
        {
            return await InstallDefaultRuntimeAsync(owner);
        }

        if (result == MessageBoxResult.No)
        {
            var optionsWindow = new OptionsWindow(OptionsWindowTab.VideoRuntime)
            {
                Owner = owner
            };
            optionsWindow.ShowDialog();
            return VideoDecoderRuntime.TryEnsureLoaded(out _);
        }

        return false;
    }

    private static async Task<bool> InstallDefaultRuntimeAsync(Window owner)
    {
        var progressWindow = new VideoRuntimeInstallWindow
        {
            Owner = owner
        };
        progressWindow.Show();

        try
        {
            var progress = new Progress<VideoRuntimeInstallProgress>(progressWindow.UpdateProgress);
            await VideoDecoderRuntime.InstallDefaultRuntimeAsync(progress);
            progressWindow.Close();
            return VideoDecoderRuntime.TryEnsureLoaded(out _);
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            MessageBox.Show(
                owner,
                $"FFmpeg 자동 설치에 실패했습니다.\n{ex.Message}\n\n옵션 창에서 경로를 직접 지정할 수 있습니다.",
                "FFmpeg 설치",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            var optionsWindow = new OptionsWindow(OptionsWindowTab.VideoRuntime)
            {
                Owner = owner
            };
            optionsWindow.ShowDialog();
            return VideoDecoderRuntime.TryEnsureLoaded(out _);
        }
    }
}
