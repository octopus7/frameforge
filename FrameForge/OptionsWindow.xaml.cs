using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace FrameForge;

public enum OptionsWindowTab
{
    FileAssociation,
    VideoRuntime
}

public partial class OptionsWindow : Window, INotifyPropertyChanged
{
    private readonly OptionsWindowTab _initialTab;
    private string _runtimePathInput = string.Empty;
    private string _runtimeStatusText = string.Empty;
    private string _runtimeDetailText = string.Empty;
    private string _runtimeInstallProgressText = "자동 설치를 실행하거나 FFmpeg DLL 폴더를 직접 지정할 수 있습니다.";
    private bool _isRuntimeInstallBusy;
    private bool _isRuntimeInstallIndeterminate = true;
    private double _runtimeInstallPercent;

    public OptionsWindow(OptionsWindowTab initialTab = OptionsWindowTab.FileAssociation)
    {
        InitializeComponent();
        _initialTab = initialTab;
        DataContext = this;

        Loaded += OptionsWindow_Loaded;
        RefreshRuntimeState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string RuntimeSourceText =>
        $"출처: {VideoDecoderRuntime.DownloadSourceName} | 버전: {VideoDecoderRuntime.DownloadVersionLabel}";

    public string DefaultRuntimeDirectoryText =>
        $"기본 설치 경로: {VideoDecoderRuntime.DefaultRuntimeDirectory}";

    public string RuntimePathInput
    {
        get => _runtimePathInput;
        set
        {
            if (_runtimePathInput == value)
            {
                return;
            }

            _runtimePathInput = value;
            OnPropertyChanged();
        }
    }

    public string RuntimeStatusText
    {
        get => _runtimeStatusText;
        private set
        {
            if (_runtimeStatusText == value)
            {
                return;
            }

            _runtimeStatusText = value;
            OnPropertyChanged();
        }
    }

    public string RuntimeDetailText
    {
        get => _runtimeDetailText;
        private set
        {
            if (_runtimeDetailText == value)
            {
                return;
            }

            _runtimeDetailText = value;
            OnPropertyChanged();
        }
    }

    public string RuntimeInstallProgressText
    {
        get => _runtimeInstallProgressText;
        private set
        {
            if (_runtimeInstallProgressText == value)
            {
                return;
            }

            _runtimeInstallProgressText = value;
            OnPropertyChanged();
        }
    }

    public bool IsRuntimeInstallIndeterminate
    {
        get => _isRuntimeInstallIndeterminate;
        private set
        {
            if (_isRuntimeInstallIndeterminate == value)
            {
                return;
            }

            _isRuntimeInstallIndeterminate = value;
            OnPropertyChanged();
        }
    }

    public double RuntimeInstallPercent
    {
        get => _runtimeInstallPercent;
        private set
        {
            if (Math.Abs(_runtimeInstallPercent - value) < 0.001)
            {
                return;
            }

            _runtimeInstallPercent = value;
            OnPropertyChanged();
        }
    }

    public bool CanEditRuntimeSettings => !_isRuntimeInstallBusy;

    private void OptionsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        OptionsTabControl.SelectedItem = _initialTab == OptionsWindowTab.VideoRuntime
            ? VideoRuntimeTab
            : ExtensionAssociationTab;

        if (_initialTab == OptionsWindowTab.VideoRuntime)
        {
            RuntimePathTextBox.Focus();
            RuntimePathTextBox.SelectAll();
        }
    }

    private void AssociateExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FileAssociationService.RegisterProjectFileAssociation();
            MessageBox.Show(
                this,
                ".ffproj 확장자 연결을 완료했습니다.",
                "옵션",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"확장자 연결에 실패했습니다.\n{ex.Message}",
                "옵션",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void BrowseRuntimePathButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "FFmpeg DLL 폴더 선택",
            InitialDirectory = string.IsNullOrWhiteSpace(RuntimePathInput)
                ? VideoDecoderRuntime.RuntimeDirectory
                : RuntimePathInput
        };

        if (dialog.ShowDialog(this) == true)
        {
            RuntimePathInput = dialog.FolderName;
        }
    }

    private void ApplyRuntimePathButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            VideoDecoderRuntime.SetRuntimeDirectoryOverride(RuntimePathInput);
            RefreshRuntimeState();
            MessageBox.Show(
                this,
                "FFmpeg 런타임 경로를 저장했습니다.",
                "옵션",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"FFmpeg 런타임 경로를 저장할 수 없습니다.\n{ex.Message}",
                "옵션",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void UseDefaultRuntimePathButton_Click(object sender, RoutedEventArgs e)
    {
        VideoDecoderRuntime.ResetToDefaultRuntimeDirectory();
        RefreshRuntimeState();
    }

    private async void InstallDefaultRuntimeButton_Click(object sender, RoutedEventArgs e)
    {
        await InstallDefaultRuntimeAsync();
    }

    private void OpenRuntimeFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folderPath = string.IsNullOrWhiteSpace(RuntimePathInput)
            ? VideoDecoderRuntime.RuntimeDirectory
            : RuntimePathInput;

        try
        {
            Directory.CreateDirectory(folderPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"폴더를 열 수 없습니다.\n{ex.Message}",
                "옵션",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task InstallDefaultRuntimeAsync()
    {
        _isRuntimeInstallBusy = true;
        OnPropertyChanged(nameof(CanEditRuntimeSettings));
        RuntimeInstallProgressText = "FFmpeg 자동 설치를 시작합니다.";
        IsRuntimeInstallIndeterminate = true;
        RuntimeInstallPercent = 0;

        try
        {
            var progress = new Progress<VideoRuntimeInstallProgress>(UpdateRuntimeInstallProgress);
            await VideoDecoderRuntime.InstallDefaultRuntimeAsync(progress);
            RefreshRuntimeState();
            RuntimeInstallProgressText = "FFmpeg 자동 설치를 완료했습니다.";
            MessageBox.Show(
                this,
                "FFmpeg 런타임 설치를 완료했습니다.",
                "옵션",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            RuntimeInstallProgressText = "FFmpeg 자동 설치에 실패했습니다.";
            MessageBox.Show(
                this,
                $"FFmpeg 자동 설치에 실패했습니다.\n{ex.Message}",
                "옵션",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isRuntimeInstallBusy = false;
            OnPropertyChanged(nameof(CanEditRuntimeSettings));
        }
    }

    private void UpdateRuntimeInstallProgress(VideoRuntimeInstallProgress progress)
    {
        RuntimeInstallProgressText = progress.StatusText;
        if (progress.TotalBytes is long totalBytes && totalBytes > 0)
        {
            IsRuntimeInstallIndeterminate = false;
            RuntimeInstallPercent = progress.BytesReceived * 100d / totalBytes;
            return;
        }

        IsRuntimeInstallIndeterminate = true;
        RuntimeInstallPercent = 0;
    }

    private void RefreshRuntimeState()
    {
        RuntimePathInput = VideoDecoderRuntime.RuntimeDirectory;

        var status = VideoDecoderRuntime.GetStatus();
        RuntimeStatusText = status.IsReady
            ? $"상태: 준비 완료 ({(status.IsCustomDirectory ? "사용자 지정 경로" : "기본 경로")})"
            : $"상태: 미설치 또는 불완전 ({(status.IsCustomDirectory ? "사용자 지정 경로" : "기본 경로")})";

        RuntimeDetailText = status.IsReady
            ? status.RuntimeDirectory
            : $"{status.StatusMessage}\n{status.RuntimeDirectory}" +
              (status.MissingFiles.Length > 0 ? $"\n누락 파일: {string.Join(", ", status.MissingFiles)}" : string.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
