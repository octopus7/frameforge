using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;

namespace FrameForge;

public partial class MakeAniGifWindow : Window, INotifyPropertyChanged
{
    private string _samplePngPath = string.Empty;
    private string _framePrefix = string.Empty;
    private string _startNumberText = string.Empty;
    private string _endNumberText = string.Empty;
    private string _sequenceStatusText = "샘플 PNG 파일을 선택하면 프리픽스와 번호 범위를 자동으로 감지합니다.";
    private int _detectedNumberWidth = 4;
    private bool _isApplyingDetectedValues;

    public MakeAniGifWindow()
    {
        InitializeComponent();
        DataContext = this;

        Loaded += MakeAniGifWindow_Loaded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SamplePngPath
    {
        get => _samplePngPath;
        set
        {
            if (string.Equals(_samplePngPath, value, StringComparison.Ordinal))
            {
                return;
            }

            _samplePngPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanGenerateGif));
            OnPropertyChanged(nameof(ValidationMessage));

            if (!_isApplyingDetectedValues)
            {
                TryDetectSequence();
            }
        }
    }

    public string FramePrefix
    {
        get => _framePrefix;
        set
        {
            if (string.Equals(_framePrefix, value, StringComparison.Ordinal))
            {
                return;
            }

            _framePrefix = value;
            NotifyStateChanged();
        }
    }

    public string StartNumberText
    {
        get => _startNumberText;
        set
        {
            if (string.Equals(_startNumberText, value, StringComparison.Ordinal))
            {
                return;
            }

            _startNumberText = value;
            NotifyStateChanged();
        }
    }

    public string EndNumberText
    {
        get => _endNumberText;
        set
        {
            if (string.Equals(_endNumberText, value, StringComparison.Ordinal))
            {
                return;
            }

            _endNumberText = value;
            NotifyStateChanged();
        }
    }

    public string SequenceStatusText
    {
        get => _sequenceStatusText;
        private set
        {
            if (string.Equals(_sequenceStatusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _sequenceStatusText = value;
            OnPropertyChanged();
        }
    }

    public bool CanGenerateGif =>
        File.Exists(SamplePngPath)
        && !string.IsNullOrWhiteSpace(FramePrefix)
        && TryGetRange(out _, out _);

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SamplePngPath))
            {
                return "샘플 PNG 파일 경로를 입력하세요.";
            }

            if (!File.Exists(SamplePngPath))
            {
                return "입력한 PNG 파일을 찾을 수 없습니다.";
            }

            if (!string.Equals(Path.GetExtension(SamplePngPath), ".png", StringComparison.OrdinalIgnoreCase))
            {
                return "PNG 파일만 선택할 수 있습니다.";
            }

            if (string.IsNullOrWhiteSpace(FramePrefix))
            {
                return "프리픽스를 입력하세요.";
            }

            if (!TryGetRange(out var rangeStart, out var rangeEnd))
            {
                return "번호 범위를 양의 정수로 입력하세요.";
            }

            if (rangeEnd < rangeStart)
            {
                return "끝 번호는 시작 번호보다 크거나 같아야 합니다.";
            }

            return "GIF 생성 버튼을 누르면 출력 위치와 파일명을 지정할 수 있습니다. 범위 안에서 누락된 번호는 자동으로 건너뜁니다.";
        }
    }

    private void MakeAniGifWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        SamplePngPathTextBox.Focus();
        SamplePngPathTextBox.SelectAll();
    }

    private void BrowseSamplePngButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "PNG Files|*.png|All Files|*.*",
            Multiselect = false,
            InitialDirectory = GetInitialDirectory()
        };

        if (dialog.ShowDialog(this) == true)
        {
            SamplePngPath = dialog.FileName;
        }
    }

    private void GenerateGifButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanGenerateGif || !TryGetRange(out var rangeStart, out var rangeEnd))
        {
            return;
        }

        var requestedFrameCount = (rangeEnd - rangeStart) + 1;

        IReadOnlyList<string> framePaths;
        try
        {
            framePaths = AniGifService.ResolveFramePaths(SamplePngPath, FramePrefix.Trim(), rangeStart, rangeEnd);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"PNG 시퀀스를 확인할 수 없습니다.\n{ex.Message}",
                "Make AniGIF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var outputDialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".gif",
            Filter = "Animated GIF|*.gif",
            InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(SamplePngPath)),
            FileName = AniGifService.BuildDefaultOutputFileName(FramePrefix.Trim(), rangeStart, rangeEnd, _detectedNumberWidth),
            OverwritePrompt = true
        };

        if (outputDialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            AniGifService.CreateAnimatedGif(outputDialog.FileName, framePaths);

            var skippedFrameCount = requestedFrameCount - framePaths.Count;
            var successMessage =
                $"AniGIF를 생성했습니다.\n{outputDialog.FileName}\n\n사용 프레임: {framePaths.Count}개";

            if (skippedFrameCount > 0)
            {
                successMessage += $"\n건너뛴 누락 프레임: {skippedFrameCount}개";
            }

            MessageBox.Show(
                this,
                successMessage,
                "Make AniGIF",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"AniGIF를 생성할 수 없습니다.\n{ex.Message}",
                "Make AniGIF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void TryDetectSequence()
    {
        if (string.IsNullOrWhiteSpace(SamplePngPath))
        {
            SequenceStatusText = "샘플 PNG 파일을 선택하면 프리픽스와 번호 범위를 자동으로 감지합니다.";
            return;
        }

        if (!File.Exists(SamplePngPath))
        {
            SequenceStatusText = "입력한 PNG 파일을 찾을 수 없습니다.";
            return;
        }

        try
        {
            var analysis = AniGifService.AnalyzeSequence(SamplePngPath);

            _isApplyingDetectedValues = true;
            try
            {
                FramePrefix = analysis.Prefix;
                StartNumberText = analysis.RangeStart.ToString();
                EndNumberText = analysis.RangeEnd.ToString();
            }
            finally
            {
                _isApplyingDetectedValues = false;
            }

            _detectedNumberWidth = analysis.NumberWidth;
            SequenceStatusText =
                $"자동 감지: 프리픽스 '{analysis.Prefix}', 범위 {analysis.RangeStart} ~ {analysis.RangeEnd}, 총 {analysis.FileCount}프레임";
        }
        catch (Exception ex)
        {
            SequenceStatusText = $"자동 감지 실패: {ex.Message}";
        }

        OnPropertyChanged(nameof(CanGenerateGif));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private bool TryGetRange(out int rangeStart, out int rangeEnd)
    {
        var hasStart = int.TryParse(StartNumberText, out rangeStart);
        var hasEnd = int.TryParse(EndNumberText, out rangeEnd);
        return hasStart && hasEnd && rangeStart > 0 && rangeEnd > 0;
    }

    private string GetInitialDirectory()
    {
        if (!string.IsNullOrWhiteSpace(SamplePngPath) && File.Exists(SamplePngPath))
        {
            var sampleDirectory = Path.GetDirectoryName(Path.GetFullPath(SamplePngPath));
            if (!string.IsNullOrWhiteSpace(sampleDirectory))
            {
                return sampleDirectory;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    }

    private void NotifyStateChanged([CallerMemberName] string? propertyName = null)
    {
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(CanGenerateGif));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
