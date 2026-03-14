using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;

namespace FrameForge;

public partial class PngSequenceExportWindow : Window, INotifyPropertyChanged
{
    private string _exportDirectory;
    private string _filePrefix;

    public PngSequenceExportWindow(string initialDirectory, string initialPrefix)
    {
        _exportDirectory = initialDirectory;
        _filePrefix = PngSequenceExportService.SanitizePrefix(initialPrefix);

        InitializeComponent();
        DataContext = this;

        Loaded += PngSequenceExportWindow_Loaded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ExportDirectory
    {
        get => _exportDirectory;
        set
        {
            if (string.Equals(_exportDirectory, value, StringComparison.Ordinal))
            {
                return;
            }

            _exportDirectory = value;
            NotifyStateChanged();
        }
    }

    public string FilePrefix
    {
        get => _filePrefix;
        set
        {
            if (string.Equals(_filePrefix, value, StringComparison.Ordinal))
            {
                return;
            }

            _filePrefix = value;
            NotifyStateChanged();
        }
    }

    public bool CanExport =>
        !string.IsNullOrWhiteSpace(ExportDirectory)
        && !string.IsNullOrWhiteSpace(FilePrefix)
        && PngSequenceExportService.IsValidPrefix(FilePrefix);

    public string PreviewFileNameText =>
        CanExport
            ? $"예상 파일명: {FilePrefix.Trim()}_0001.png"
            : "예상 파일명: prefix_0001.png";

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ExportDirectory))
            {
                return "출력 폴더를 지정하세요.";
            }

            if (string.IsNullOrWhiteSpace(FilePrefix))
            {
                return "파일 프리픽스를 입력하세요.";
            }

            if (!PngSequenceExportService.IsValidPrefix(FilePrefix))
            {
                return "프리픽스에 사용할 수 없는 문자가 있습니다.";
            }

            return "프레임 순서대로 PNG 파일이 새 번호로 저장됩니다.";
        }
    }

    private void PngSequenceExportWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FilePrefix))
        {
            ExportDirectoryTextBox.Focus();
            ExportDirectoryTextBox.SelectAll();
            return;
        }

        FilePrefixTextBox.Focus();
        FilePrefixTextBox.SelectAll();
    }

    private void BrowseDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "PNG 시퀀스 출력 폴더 선택",
            InitialDirectory = string.IsNullOrWhiteSpace(ExportDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                : ExportDirectory
        };

        if (dialog.ShowDialog(this) == true)
        {
            ExportDirectory = dialog.FolderName;
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanExport)
        {
            return;
        }

        ExportDirectory = Path.GetFullPath(ExportDirectory.Trim());
        FilePrefix = FilePrefix.Trim();
        DialogResult = true;
    }

    private void NotifyStateChanged([CallerMemberName] string? propertyName = null)
    {
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(PreviewFileNameText));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
