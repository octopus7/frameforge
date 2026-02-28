using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FrameForge;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    private readonly DispatcherTimer _playbackTimer;
    private SpriteSheetImportWindow? _sheetImportWindow;
    private int _selectedFrameIndex = -1;
    private bool _isPlaying;
    private double _zoomFactor = 1.0;
    private double _thumbnailHeight = 120;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _playbackTimer.Tick += PlaybackTimer_Tick;

        Loaded += (_, _) => Keyboard.Focus(this);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AnimationFrame> Frames { get; } = [];

    public int SelectedFrameIndex
    {
        get => _selectedFrameIndex;
        set
        {
            var normalized = NormalizeSelection(value);
            if (_selectedFrameIndex == normalized)
            {
                return;
            }

            _selectedFrameIndex = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentFrameImage));
            OnPropertyChanged(nameof(CurrentFrameSummary));
            EnsureCurrentFrameVisibleAndFocused();
        }
    }

    public BitmapSource? CurrentFrameImage =>
        SelectedFrameIndex >= 0 && SelectedFrameIndex < Frames.Count ? Frames[SelectedFrameIndex].Image : null;

    public string CurrentFrameSummary =>
        HasFrames ? $"Frame {SelectedFrameIndex + 1}/{Frames.Count} - {Frames[SelectedFrameIndex].Name}" : "Frame 0/0";

    public string EmptyFrameMessage => IsKoreanUiCulture
        ? "하단 영역에 프레임 이미지를 추가하면 현재 프레임이 여기에 표시됩니다."
        : "Add frame images in the bottom area to show the current frame here.";

    public bool HasFrames => Frames.Count > 0;

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;
            OnPropertyChanged();
        }
    }

    // Future zoom UI can bind into this value. Current default is 1.0 (original pixels).
    public double ZoomFactor
    {
        get => _zoomFactor;
        private set
        {
            if (Math.Abs(_zoomFactor - value) < 0.0001)
            {
                return;
            }

            _zoomFactor = value;
            OnPropertyChanged();
        }
    }

    public double ThumbnailHeight
    {
        get => _thumbnailHeight;
        private set
        {
            if (Math.Abs(_thumbnailHeight - value) < 0.1)
            {
                return;
            }

            _thumbnailHeight = value;
            OnPropertyChanged();
        }
    }

    public void SetZoomFactor(double zoomFactor)
    {
        ZoomFactor = Math.Clamp(zoomFactor, 0.1, 8.0);
    }

    public void AddFrame(BitmapSource image, string? name = null, string? sourcePath = null, bool focusAddedFrame = true)
    {
        var frameName = string.IsNullOrWhiteSpace(name) ? $"Frame_{Frames.Count + 1:000}" : name;

        if (image is Freezable freezable && freezable.CanFreeze && !freezable.IsFrozen)
        {
            freezable.Freeze();
        }

        Frames.Add(new AnimationFrame(frameName, image, sourcePath));
        OnPropertyChanged(nameof(HasFrames));

        if (focusAddedFrame)
        {
            SelectedFrameIndex = Frames.Count - 1;
            return;
        }

        if (SelectedFrameIndex < 0)
        {
            SelectedFrameIndex = 0;
        }
        else
        {
            OnPropertyChanged(nameof(CurrentFrameSummary));
            EnsureCurrentFrameVisibleAndFocused();
        }
    }

    private static bool IsKoreanUiCulture =>
        string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ko", StringComparison.OrdinalIgnoreCase);

    private int NormalizeSelection(int requestedIndex)
    {
        if (Frames.Count == 0)
        {
            return -1;
        }

        return Math.Clamp(requestedIndex, 0, Frames.Count - 1);
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

    private static bool IsSupportedImagePath(string path)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(extension) && SupportedImageExtensions.Contains(extension);
    }

    private void AddFramesFromPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!IsSupportedImagePath(path))
            {
                continue;
            }

            try
            {
                var image = LoadBitmap(path);
                AddFrame(image, Path.GetFileName(path), path, focusAddedFrame: false);
            }
            catch
            {
                // Continue importing valid files if one file fails.
            }
        }
    }

    private void MoveSelection(int delta, bool wrap)
    {
        if (!HasFrames)
        {
            return;
        }

        var next = SelectedFrameIndex < 0 ? 0 : SelectedFrameIndex + delta;

        if (wrap)
        {
            next = (next % Frames.Count + Frames.Count) % Frames.Count;
        }
        else
        {
            next = Math.Clamp(next, 0, Frames.Count - 1);
        }

        SelectedFrameIndex = next;
    }

    private void TogglePlayback()
    {
        if (!HasFrames)
        {
            return;
        }

        if (IsPlaying)
        {
            _playbackTimer.Stop();
            IsPlaying = false;
            return;
        }

        _playbackTimer.Start();
        IsPlaying = true;
    }

    private void StopPlayback()
    {
        if (!IsPlaying)
        {
            return;
        }

        _playbackTimer.Stop();
        IsPlaying = false;
    }

    private void EnsureCurrentFrameVisibleAndFocused()
    {
        if (!HasFrames || SelectedFrameIndex < 0)
        {
            return;
        }

        var frame = Frames[SelectedFrameIndex];
        ThumbnailList.ScrollIntoView(frame);

        Dispatcher.BeginInvoke(() =>
        {
            if (ThumbnailList.ItemContainerGenerator.ContainerFromIndex(SelectedFrameIndex) is IInputElement item)
            {
                Keyboard.Focus(item);
            }
        }, DispatcherPriority.Background);
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        MoveSelection(1, wrap: true);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left)
        {
            MoveSelection(-1, wrap: false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right)
        {
            MoveSelection(1, wrap: false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            TogglePlayback();
            e.Handled = true;
        }
    }

    private void PreviousFrameButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelection(-1, wrap: false);
    }

    private void NextFrameButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelection(1, wrap: false);
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayback();
    }

    private void AddFramesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            CheckFileExists = true,
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All Files|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            AddFramesFromPaths(dialog.FileNames);
        }
    }

    private void ImportSingleImageSheetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_sheetImportWindow is { IsLoaded: true })
        {
            _sheetImportWindow.Activate();
            _sheetImportWindow.Focus();
            return;
        }

        _sheetImportWindow = new SpriteSheetImportWindow(OnSheetFrameCaptured)
        {
            Owner = this
        };
        _sheetImportWindow.Closed += (_, _) => _sheetImportWindow = null;
        _sheetImportWindow.Show();
    }

    private void OnSheetFrameCaptured(BitmapSource frame)
    {
        AddFrame(frame, $"Sheet_{Frames.Count + 1:000}", focusAddedFrame: true);
    }

    private void ThumbnailList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThumbnailList.SelectedIndex >= 0 && ThumbnailList.SelectedIndex != SelectedFrameIndex)
        {
            SelectedFrameIndex = ThumbnailList.SelectedIndex;
            return;
        }

        if (HasFrames && ThumbnailList.SelectedIndex < 0)
        {
            ThumbnailList.SelectedIndex = NormalizeSelection(SelectedFrameIndex);
        }
    }

    private void ThumbnailList_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void ThumbnailList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] droppedFiles)
        {
            AddFramesFromPaths(droppedFiles);
        }
    }

    private void ThumbnailList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ThumbnailHeight = Math.Max(48, e.NewSize.Height - 36);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopPlayback();
        _sheetImportWindow?.Close();
        base.OnClosed(e);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class AnimationFrame
{
    public AnimationFrame(string name, BitmapSource image, string? sourcePath = null)
    {
        Name = name;
        SourcePath = sourcePath;
        Image = image;
    }

    public string Name { get; }
    public string? SourcePath { get; }
    public BitmapSource Image { get; }
}

