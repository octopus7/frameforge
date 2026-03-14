using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace FrameForge;

public partial class VideoRuntimeInstallWindow : Window, INotifyPropertyChanged
{
    private string _statusText = "다운로드 준비 중입니다.";
    private bool _isIndeterminate = true;
    private double _progressPercent;

    public VideoRuntimeInstallWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set
        {
            if (_isIndeterminate == value)
            {
                return;
            }

            _isIndeterminate = value;
            OnPropertyChanged();
        }
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set
        {
            if (_progressPercent == value)
            {
                return;
            }

            _progressPercent = value;
            OnPropertyChanged();
        }
    }

    public void UpdateProgress(VideoRuntimeInstallProgress progress)
    {
        StatusText = progress.StatusText;
        if (progress.TotalBytes is long totalBytes && totalBytes > 0)
        {
            IsIndeterminate = false;
            ProgressPercent = progress.BytesReceived * 100d / totalBytes;
        }
        else
        {
            IsIndeterminate = true;
            ProgressPercent = 0;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
