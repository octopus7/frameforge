using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace FrameForge;

public sealed class VideoImportFrameItem : INotifyPropertyChanged
{
    private string _displayName;
    private BitmapSource _image;
    private bool _isActive;

    public VideoImportFrameItem(int sourceIndex, TimeSpan timestamp, string displayName, BitmapSource image)
    {
        SourceIndex = sourceIndex;
        Timestamp = timestamp;
        _displayName = displayName;
        _image = image;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int SourceIndex { get; }
    public TimeSpan Timestamp { get; }

    public string DisplayName
    {
        get => _displayName;
        private set
        {
            if (string.Equals(_displayName, value, StringComparison.Ordinal))
            {
                return;
            }

            _displayName = value;
            OnPropertyChanged();
        }
    }

    public BitmapSource Image
    {
        get => _image;
        private set
        {
            if (ReferenceEquals(_image, value))
            {
                return;
            }

            _image = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Subtitle));
        }
    }

    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            OnPropertyChanged();
        }
    }

    public string Subtitle => $"{FormatTimestamp(Timestamp)} · {Image.PixelWidth}x{Image.PixelHeight}";

    public void SetDisplayName(string displayName)
    {
        DisplayName = displayName;
    }

    public void SetImage(BitmapSource image)
    {
        Image = image;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }

    private static string FormatTimestamp(TimeSpan timestamp)
    {
        return timestamp.TotalHours >= 1
            ? timestamp.ToString(@"hh\:mm\:ss\.fff")
            : timestamp.ToString(@"mm\:ss\.fff");
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class VideoImportFrameResult
{
    public VideoImportFrameResult(string name, BitmapSource image)
    {
        Name = name;
        Image = image;
    }

    public string Name { get; }
    public BitmapSource Image { get; }
}

public sealed class VideoImportResult
{
    public VideoImportResult(string sourceVideoPath, IReadOnlyList<VideoImportFrameResult> frames)
    {
        SourceVideoPath = sourceVideoPath;
        Frames = frames;
    }

    public string SourceVideoPath { get; }
    public IReadOnlyList<VideoImportFrameResult> Frames { get; }
}

public sealed class VideoImportSession : INotifyPropertyChanged
{
    private int _activeFrameIndex = -1;
    private int _selectionAnchorIndex = -1;
    private Rect _cropSelectionDip = Rect.Empty;
    private bool _isLoading;
    private string? _loadError;
    private string? _sourceVideoPath;
    private string _sourceVideoStem = "Video";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<VideoImportFrameItem> Frames { get; } = [];

    public int ActiveFrameIndex
    {
        get => _activeFrameIndex;
        private set
        {
            var normalized = Frames.Count == 0 ? -1 : Math.Clamp(value, 0, Frames.Count - 1);
            if (_activeFrameIndex == normalized)
            {
                return;
            }

            if (_activeFrameIndex >= 0 && _activeFrameIndex < Frames.Count)
            {
                Frames[_activeFrameIndex].SetActive(false);
            }

            _activeFrameIndex = normalized;

            if (_activeFrameIndex >= 0 && _activeFrameIndex < Frames.Count)
            {
                Frames[_activeFrameIndex].SetActive(true);
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveFrameImage));
            OnPropertyChanged(nameof(ActiveFrameItem));
            OnPropertyChanged(nameof(HasActiveFrame));
        }
    }

    public int SelectionAnchorIndex
    {
        get => _selectionAnchorIndex;
        private set
        {
            if (_selectionAnchorIndex == value)
            {
                return;
            }

            _selectionAnchorIndex = value;
            OnPropertyChanged();
        }
    }

    public Rect CropSelectionDip
    {
        get => _cropSelectionDip;
        private set
        {
            if (_cropSelectionDip == value)
            {
                return;
            }

            _cropSelectionDip = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCropSelection));
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public string? LoadError
    {
        get => _loadError;
        private set
        {
            if (string.Equals(_loadError, value, StringComparison.Ordinal))
            {
                return;
            }

            _loadError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLoadError));
        }
    }

    public string? SourceVideoPath
    {
        get => _sourceVideoPath;
        private set
        {
            if (string.Equals(_sourceVideoPath, value, StringComparison.Ordinal))
            {
                return;
            }

            _sourceVideoPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SourceVideoName));
        }
    }

    public string SourceVideoName => string.IsNullOrWhiteSpace(SourceVideoPath) ? string.Empty : Path.GetFileName(SourceVideoPath);

    public string SourceVideoStem
    {
        get => _sourceVideoStem;
        private set
        {
            if (string.Equals(_sourceVideoStem, value, StringComparison.Ordinal))
            {
                return;
            }

            _sourceVideoStem = value;
            OnPropertyChanged();
        }
    }

    public bool HasFrames => Frames.Count > 0;
    public bool HasActiveFrame => ActiveFrameIndex >= 0 && ActiveFrameIndex < Frames.Count;
    public bool HasCropSelection => CropSelectionDip.Width >= 2 && CropSelectionDip.Height >= 2;
    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);
    public BitmapSource? ActiveFrameImage => HasActiveFrame ? Frames[ActiveFrameIndex].Image : null;
    public VideoImportFrameItem? ActiveFrameItem => HasActiveFrame ? Frames[ActiveFrameIndex] : null;

    public void BeginLoading(string sourceVideoPath)
    {
        SourceVideoPath = sourceVideoPath;
        SourceVideoStem = SanitizeStem(Path.GetFileNameWithoutExtension(sourceVideoPath));
        LoadError = null;
        IsLoading = true;
        ReplaceFrames([]);
        ClearCropSelection();
    }

    public void CompleteLoading(IReadOnlyList<VideoCapturedFrame> capturedFrames)
    {
        ArgumentNullException.ThrowIfNull(capturedFrames);

        var importedFrames = capturedFrames
            .Select(frame => new VideoImportFrameItem(frame.SourceIndex, frame.Timestamp, string.Empty, frame.Image))
            .ToArray();

        ReplaceFrames(importedFrames);
        RenumberFrames();
        ActiveFrameIndex = importedFrames.Length > 0 ? 0 : -1;
        SelectionAnchorIndex = importedFrames.Length > 0 ? 0 : -1;
        LoadError = importedFrames.Length == 0 ? "영상에서 프레임을 찾지 못했습니다." : null;
        IsLoading = false;
        ClearCropSelection();
    }

    public void FailLoading(string errorMessage)
    {
        ReplaceFrames([]);
        ActiveFrameIndex = -1;
        SelectionAnchorIndex = -1;
        ClearCropSelection();
        LoadError = errorMessage;
        IsLoading = false;
    }

    public void SetActiveFrameIndex(int index)
    {
        ActiveFrameIndex = index;
    }

    public void ApplySelectionInteraction(int clickedIndex, ModifierKeys modifiers, IReadOnlyList<int> selectedIndices)
    {
        if (clickedIndex < 0 || clickedIndex >= Frames.Count)
        {
            return;
        }

        var normalizedSelection = NormalizeSelectionIndices(selectedIndices);
        var hasShift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var hasControl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (hasShift)
        {
            if (SelectionAnchorIndex < 0 || SelectionAnchorIndex >= Frames.Count)
            {
                SelectionAnchorIndex = clickedIndex;
            }

            ActiveFrameIndex = clickedIndex;
            return;
        }

        if (hasControl)
        {
            if (normalizedSelection.Contains(clickedIndex))
            {
                SelectionAnchorIndex = clickedIndex;
                ActiveFrameIndex = clickedIndex;
                return;
            }

            if (ActiveFrameIndex == clickedIndex)
            {
                var replacement = FindNearestSelectionIndex(normalizedSelection, clickedIndex);
                ActiveFrameIndex = replacement >= 0 ? replacement : clickedIndex;
            }
            else if (!HasActiveFrame)
            {
                var replacement = FindNearestSelectionIndex(normalizedSelection, clickedIndex);
                ActiveFrameIndex = replacement >= 0 ? replacement : clickedIndex;
            }

            return;
        }

        SelectionAnchorIndex = clickedIndex;
        ActiveFrameIndex = clickedIndex;
    }

    public void SyncActiveFrame(IReadOnlyList<int> selectedIndices)
    {
        if (HasActiveFrame)
        {
            return;
        }

        if (Frames.Count == 0)
        {
            ActiveFrameIndex = -1;
            SelectionAnchorIndex = -1;
            return;
        }

        var normalizedSelection = NormalizeSelectionIndices(selectedIndices);
        ActiveFrameIndex = normalizedSelection.Count > 0 ? normalizedSelection[0] : 0;
        if (SelectionAnchorIndex < 0)
        {
            SelectionAnchorIndex = ActiveFrameIndex;
        }
    }

    public bool RemoveFrames(IReadOnlyList<int> selectedIndices)
    {
        var normalizedSelection = NormalizeSelectionIndices(selectedIndices);
        if (normalizedSelection.Count == 0)
        {
            return false;
        }

        var survivingOldIndices = Enumerable.Range(0, Frames.Count)
            .Except(normalizedSelection)
            .ToArray();

        var nextActiveIndex = -1;
        if (survivingOldIndices.Length > 0)
        {
            if (survivingOldIndices.Contains(ActiveFrameIndex))
            {
                nextActiveIndex = Array.IndexOf(survivingOldIndices, ActiveFrameIndex);
            }
            else
            {
                var fallbackOldIndex = survivingOldIndices.FirstOrDefault(index => index > ActiveFrameIndex);
                if (fallbackOldIndex == 0 && ActiveFrameIndex >= survivingOldIndices[^1])
                {
                    fallbackOldIndex = survivingOldIndices[^1];
                }
                else if (!survivingOldIndices.Contains(fallbackOldIndex))
                {
                    fallbackOldIndex = survivingOldIndices[^1];
                }

                nextActiveIndex = Array.IndexOf(survivingOldIndices, fallbackOldIndex);
            }
        }

        for (var i = normalizedSelection.Count - 1; i >= 0; i--)
        {
            Frames.RemoveAt(normalizedSelection[i]);
        }

        OnPropertyChanged(nameof(HasFrames));

        SelectionAnchorIndex = Frames.Count == 0
            ? -1
            : Math.Clamp(FindNearestRemainingIndex(normalizedSelection, SelectionAnchorIndex), 0, Frames.Count - 1);
        RenumberFrames();
        ActiveFrameIndex = nextActiveIndex;
        OnPropertyChanged(nameof(ActiveFrameImage));
        OnPropertyChanged(nameof(ActiveFrameItem));
        return true;
    }

    public void SetCropSelection(Rect selectionDip)
    {
        CropSelectionDip = selectionDip.Width < 2 || selectionDip.Height < 2
            ? Rect.Empty
            : selectionDip;
    }

    public void ClearCropSelection()
    {
        CropSelectionDip = Rect.Empty;
    }

    public bool ApplyCrop(Int32Rect pixelRect)
    {
        if (Frames.Count == 0 || pixelRect.IsEmpty || pixelRect.Width <= 0 || pixelRect.Height <= 0)
        {
            return false;
        }

        foreach (var frame in Frames)
        {
            var cropped = new CroppedBitmap(frame.Image, pixelRect);
            cropped.Freeze();
            frame.SetImage(cropped);
        }

        ClearCropSelection();
        OnPropertyChanged(nameof(ActiveFrameImage));
        OnPropertyChanged(nameof(ActiveFrameItem));
        return true;
    }

    public VideoImportResult CreateResult()
    {
        if (string.IsNullOrWhiteSpace(SourceVideoPath))
        {
            throw new InvalidOperationException("영상 경로가 설정되지 않았습니다.");
        }

        var frames = Frames
            .Select(frame => new VideoImportFrameResult(frame.DisplayName, frame.Image))
            .ToArray();
        return new VideoImportResult(SourceVideoPath, frames);
    }

    private void ReplaceFrames(IEnumerable<VideoImportFrameItem> frames)
    {
        Frames.Clear();
        foreach (var frame in frames)
        {
            Frames.Add(frame);
        }

        OnPropertyChanged(nameof(HasFrames));
        OnPropertyChanged(nameof(ActiveFrameImage));
        OnPropertyChanged(nameof(ActiveFrameItem));
        ActiveFrameIndex = Frames.Count > 0 ? Math.Clamp(ActiveFrameIndex, 0, Frames.Count - 1) : -1;
    }

    private void RenumberFrames()
    {
        for (var i = 0; i < Frames.Count; i++)
        {
            Frames[i].SetDisplayName($"{SourceVideoStem}_{i + 1:0000}");
        }
    }

    private static List<int> NormalizeSelectionIndices(IReadOnlyList<int> selectedIndices)
    {
        return selectedIndices
            .Where(index => index >= 0)
            .Distinct()
            .OrderBy(index => index)
            .ToList();
    }

    private static int FindNearestSelectionIndex(IReadOnlyList<int> selectedIndices, int referenceIndex)
    {
        if (selectedIndices.Count == 0)
        {
            return -1;
        }

        return selectedIndices
            .OrderBy(index => Math.Abs(index - referenceIndex))
            .ThenBy(index => index)
            .First();
    }

    private static int FindNearestRemainingIndex(IReadOnlyList<int> removedIndices, int referenceIndex)
    {
        if (referenceIndex < 0)
        {
            return 0;
        }

        var removedBefore = removedIndices.Count(index => index < referenceIndex);
        var adjusted = referenceIndex - removedBefore;
        return Math.Max(0, adjusted);
    }

    private static string SanitizeStem(string? stem)
    {
        return string.IsNullOrWhiteSpace(stem) ? "Video" : stem.Trim();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
