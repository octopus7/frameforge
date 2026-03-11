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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FrameForge;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const string BaseWindowTitle = "FrameForge Tool";
    private const string FrameDragDataFormat = "FrameForge.AnimationFrame";
    private const string ProjectFileFilter = "FrameForge Project|*.ffproj|All Files|*.*";

    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    private readonly DispatcherTimer _playbackTimer;
    private SpriteSheetImportWindow? _sheetImportWindow;
    private int _selectedFrameIndex = -1;
    private int _canvasWidth;
    private int _canvasHeight;
    private bool _isPlaying;
    private bool _isLoopEnabled;
    private bool _isPreviewDragging;
    private double _zoomFactor = 1.0;
    private double _thumbnailHeight = 120;
    private Point _previewDragStartPoint;
    private Rect _previewSelectionDip = Rect.Empty;
    private CanvasViewportLayout _previewLayout = CanvasViewportLayout.Empty;
    private Point _thumbnailDragStartPoint;
    private AnimationFrame? _thumbnailDragSourceFrame;
    private string? _currentProjectPath;
    private bool _isDirty;
    private bool _suppressDirtyTracking;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _playbackTimer.Tick += PlaybackTimer_Tick;

        UpdateWindowTitle();
        Loaded += (_, _) =>
        {
            Keyboard.Focus(this);
            UpdatePreviewWorkspaceLayout();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AnimationFrame> Frames { get; } = [];

    public int CanvasWidth => _canvasWidth;

    public int CanvasHeight => _canvasHeight;

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
            ClearPreviewSelection();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentFrameImage));
            OnPropertyChanged(nameof(CurrentFrameSummary));
            UpdatePreviewWorkspaceLayout();
            EnsureCurrentFrameVisibleAndFocused();
        }
    }

    public BitmapSource? CurrentFrameImage => CurrentFrame?.Image;

    public string CurrentFrameSummary =>
        HasFrames
            ? $"Frame {SelectedFrameIndex + 1}/{Frames.Count} - {CurrentFrame!.Name} | Canvas {CanvasWidth}x{CanvasHeight}"
            : HasCanvas
                ? $"Frame 0/0 | Canvas {CanvasWidth}x{CanvasHeight}"
                : "Frame 0/0";

    public string EmptyFrameMessage => IsKoreanUiCulture
        ? "하단 영역에 프레임 이미지를 추가하면 현재 프레임이 여기에 표시됩니다."
        : "Add frame images in the bottom area to show the current frame here.";

    public bool HasFrames => Frames.Count > 0;

    public bool HasCanvas => CanvasWidth > 0 && CanvasHeight > 0;

    public bool HasPreviewSelection => HasValidPreviewSelection(_previewSelectionDip);

    public bool CanCropCanvas => HasCanvas && HasPreviewSelection;

    public bool IsLoopEnabled
    {
        get => _isLoopEnabled;
        set
        {
            if (_isLoopEnabled == value)
            {
                return;
            }

            _isLoopEnabled = value;
            OnPropertyChanged();

            if (!_suppressDirtyTracking)
            {
                MarkDirty();
            }
        }
    }

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
        var insertedIndex = InsertFrameAtIndex(Frames.Count, image, name, sourcePath);

        if (focusAddedFrame)
        {
            SelectedFrameIndex = insertedIndex;
            return;
        }

        if (SelectedFrameIndex < 0)
        {
            SelectedFrameIndex = 0;
        }
        else
        {
            RefreshCurrentFrameState(includeImageProperty: false);
            EnsureCurrentFrameVisibleAndFocused();
        }
    }

    private AnimationFrame? CurrentFrame =>
        SelectedFrameIndex >= 0 && SelectedFrameIndex < Frames.Count ? Frames[SelectedFrameIndex] : null;

    private static bool IsKoreanUiCulture =>
        string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ko", StringComparison.OrdinalIgnoreCase);

    private void UpdateWindowTitle()
    {
        var projectLabel = string.IsNullOrWhiteSpace(_currentProjectPath)
            ? "Untitled"
            : Path.GetFileName(_currentProjectPath);
        var dirtyMarker = _isDirty ? "*" : string.Empty;
        Title = $"{BaseWindowTitle} - {projectLabel}{dirtyMarker}";
    }

    private void MarkDirty()
    {
        if (_suppressDirtyTracking || _isDirty)
        {
            return;
        }

        _isDirty = true;
        UpdateWindowTitle();
    }

    private void MarkClean()
    {
        if (!_isDirty)
        {
            return;
        }

        _isDirty = false;
        UpdateWindowTitle();
    }

    private void SetCurrentProjectPath(string? projectPath)
    {
        _currentProjectPath = projectPath;
        UpdateWindowTitle();
    }

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

    private void SetCanvasSize(int width, int height)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);

        if (_canvasWidth == width && _canvasHeight == height)
        {
            return;
        }

        _canvasWidth = width;
        _canvasHeight = height;
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(HasCanvas));
        OnPropertyChanged(nameof(CurrentFrameSummary));
        UpdatePreviewWorkspaceLayout();

        if (!_suppressDirtyTracking)
        {
            MarkDirty();
        }
    }

    private void ResetCanvas()
    {
        SetCanvasSize(0, 0);
    }

    private void RefreshCurrentFrameState(bool includeImageProperty)
    {
        if (includeImageProperty)
        {
            OnPropertyChanged(nameof(CurrentFrameImage));
        }

        OnPropertyChanged(nameof(CurrentFrameSummary));
        UpdatePreviewWorkspaceLayout();
    }

    private void UpdatePreviewWorkspaceLayout()
    {
        _previewLayout = CanvasLayoutHelper.CalculateViewport(CanvasWidth, CanvasHeight, CurrentFrame);
        PreviewOverlayCanvas.Width = _previewLayout.WorkspaceWidth;
        PreviewOverlayCanvas.Height = _previewLayout.WorkspaceHeight;

        if (!HasCanvas || _previewLayout.CanvasRect.IsEmpty)
        {
            PreviewCanvasBounds.Visibility = Visibility.Collapsed;
        }
        else
        {
            PreviewCanvasBounds.Visibility = Visibility.Visible;
            PreviewCanvasBounds.Width = _previewLayout.CanvasRect.Width;
            PreviewCanvasBounds.Height = _previewLayout.CanvasRect.Height;
            Canvas.SetLeft(PreviewCanvasBounds, _previewLayout.CanvasRect.X);
            Canvas.SetTop(PreviewCanvasBounds, _previewLayout.CanvasRect.Y);
        }

        var currentFrameImage = CurrentFrameImage;
        PreviewFrameImage.Source = currentFrameImage;

        if (currentFrameImage is null || _previewLayout.FrameRect.IsEmpty)
        {
            PreviewFrameImage.Visibility = Visibility.Collapsed;
            PreviewFrameImage.Width = 0;
            PreviewFrameImage.Height = 0;
            return;
        }

        PreviewFrameImage.Visibility = Visibility.Visible;
        PreviewFrameImage.Width = currentFrameImage.PixelWidth;
        PreviewFrameImage.Height = currentFrameImage.PixelHeight;
        Canvas.SetLeft(PreviewFrameImage, _previewLayout.FrameRect.X);
        Canvas.SetTop(PreviewFrameImage, _previewLayout.FrameRect.Y);
    }

    private int InsertFrameAtIndex(int insertionIndex, BitmapSource image, string? name = null, string? sourcePath = null)
    {
        var frameName = string.IsNullOrWhiteSpace(name) ? $"Frame_{Frames.Count + 1:000}" : name;

        if (image is Freezable freezable && freezable.CanFreeze && !freezable.IsFrozen)
        {
            freezable.Freeze();
        }

        if (!HasCanvas)
        {
            SetCanvasSize(image.PixelWidth, image.PixelHeight);
        }

        var position = Frames.Count == 0
            ? new Point(0, 0)
            : CanvasLayoutHelper.CenterFrame(CanvasWidth, CanvasHeight, image.PixelWidth, image.PixelHeight);
        var normalizedInsertionIndex = Math.Clamp(insertionIndex, 0, Frames.Count);
        Frames.Insert(
            normalizedInsertionIndex,
            new AnimationFrame(frameName, image, (int)position.X, (int)position.Y, sourcePath));
        OnPropertyChanged(nameof(HasFrames));
        RefreshCurrentFrameState(includeImageProperty: false);

        if (!_suppressDirtyTracking)
        {
            MarkDirty();
        }

        return normalizedInsertionIndex;
    }

    private static bool HasValidPreviewSelection(Rect selectionRect)
    {
        return selectionRect.Width > 0 && selectionRect.Height > 0;
    }

    private void NotifyPreviewSelectionStateChanged()
    {
        OnPropertyChanged(nameof(HasPreviewSelection));
        OnPropertyChanged(nameof(CanCropCanvas));
    }

    private void ClearPreviewSelection()
    {
        _isPreviewDragging = false;
        _previewSelectionDip = Rect.Empty;

        if (PreviewOverlayCanvas.IsMouseCaptured)
        {
            PreviewOverlayCanvas.ReleaseMouseCapture();
        }

        PreviewSelectionRectangle.Visibility = Visibility.Collapsed;
        NotifyPreviewSelectionStateChanged();
    }

    private void UpdatePreviewSelectionVisual()
    {
        if (!HasValidPreviewSelection(_previewSelectionDip))
        {
            PreviewSelectionRectangle.Visibility = Visibility.Collapsed;
            NotifyPreviewSelectionStateChanged();
            return;
        }

        PreviewSelectionRectangle.Visibility = Visibility.Visible;
        PreviewSelectionRectangle.Width = _previewSelectionDip.Width;
        PreviewSelectionRectangle.Height = _previewSelectionDip.Height;
        Canvas.SetLeft(PreviewSelectionRectangle, _previewSelectionDip.X);
        Canvas.SetTop(PreviewSelectionRectangle, _previewSelectionDip.Y);
        NotifyPreviewSelectionStateChanged();
    }

    private static Point ClampPointToRect(Point point, Rect rect)
    {
        return new Point(
            Math.Clamp(point.X, rect.Left, rect.Right),
            Math.Clamp(point.Y, rect.Top, rect.Bottom));
    }

    private bool CropCurrentCanvas()
    {
        if (!CanCropCanvas || !HasCanvas || _previewLayout.CanvasRect.IsEmpty)
        {
            return false;
        }

        StopPlayback();

        var pixelRect = CanvasLayoutHelper.ClampSelectionToCanvas(
            _previewSelectionDip,
            _previewLayout.CanvasRect,
            CanvasWidth,
            CanvasHeight);
        if (pixelRect.IsEmpty || pixelRect.Width <= 0 || pixelRect.Height <= 0)
        {
            return false;
        }

        if (pixelRect.X == 0
            && pixelRect.Y == 0
            && pixelRect.Width == CanvasWidth
            && pixelRect.Height == CanvasHeight)
        {
            ClearPreviewSelection();
            return false;
        }

        var shiftedFrames = CanvasLayoutHelper.OffsetFrames(Frames, -pixelRect.X, -pixelRect.Y);
        for (var i = 0; i < shiftedFrames.Count; i++)
        {
            Frames[i] = shiftedFrames[i];
        }

        SetCanvasSize(pixelRect.Width, pixelRect.Height);
        ThumbnailList.SelectedIndex = SelectedFrameIndex;

        ClearPreviewSelection();
        RefreshCurrentFrameState(includeImageProperty: false);

        return true;
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

    private bool TryMoveCurrentFrameByArrowKey(Key key, ModifierKeys modifiers)
    {
        var currentFrame = CurrentFrame;
        if (currentFrame is null)
        {
            return false;
        }

        var step = (modifiers & ModifierKeys.Control) == ModifierKeys.Control ? 10 : 1;
        var (deltaX, deltaY) = key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => (0, 0)
        };

        if (deltaX == 0 && deltaY == 0)
        {
            return false;
        }

        StopPlayback();
        Frames[SelectedFrameIndex] = currentFrame.WithPosition(currentFrame.X + deltaX, currentFrame.Y + deltaY);
        ThumbnailList.SelectedIndex = SelectedFrameIndex;
        RefreshCurrentFrameState(includeImageProperty: false);

        if (!_suppressDirtyTracking)
        {
            MarkDirty();
        }

        return true;
    }

    private void RemoveSelectedFrame()
    {
        if (!HasFrames || SelectedFrameIndex < 0 || SelectedFrameIndex >= Frames.Count)
        {
            return;
        }

        ClearPreviewSelection();

        var removedIndex = SelectedFrameIndex;
        Frames.RemoveAt(removedIndex);
        OnPropertyChanged(nameof(HasFrames));

        if (Frames.Count == 0)
        {
            StopPlayback();
            ResetCanvas();
            SelectedFrameIndex = -1;
            return;
        }

        var nextIndex = Math.Clamp(removedIndex, 0, Frames.Count - 1);
        if (_selectedFrameIndex == nextIndex)
        {
            ClearPreviewSelection();
            OnPropertyChanged(nameof(CurrentFrameImage));
            OnPropertyChanged(nameof(CurrentFrameSummary));
            UpdatePreviewWorkspaceLayout();
            EnsureCurrentFrameVisibleAndFocused();
        }
        else
        {
            SelectedFrameIndex = nextIndex;
        }

        if (!_suppressDirtyTracking)
        {
            MarkDirty();
        }
    }

    private void MoveFrameByInsertionIndex(int sourceIndex, int insertionIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= Frames.Count)
        {
            return;
        }

        var targetInsertionIndex = Math.Clamp(insertionIndex, 0, Frames.Count);
        var targetIndex = targetInsertionIndex > sourceIndex ? targetInsertionIndex - 1 : targetInsertionIndex;

        if (targetIndex == sourceIndex)
        {
            return;
        }

        var frame = Frames[sourceIndex];
        Frames.RemoveAt(sourceIndex);
        Frames.Insert(targetIndex, frame);
        SelectedFrameIndex = targetIndex;

        if (!_suppressDirtyTracking)
        {
            MarkDirty();
        }
    }

    private int GetThumbnailInsertionIndexFromDropPosition(DragEventArgs e)
    {
        if (Frames.Count == 0)
        {
            return 0;
        }

        var dropTarget = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (dropTarget is null)
        {
            var dropPoint = e.GetPosition(ThumbnailList);
            return dropPoint.X < ThumbnailList.ActualWidth * 0.5 ? 0 : Frames.Count;
        }

        var index = ThumbnailList.ItemContainerGenerator.IndexFromContainer(dropTarget);
        if (index < 0)
        {
            return Frames.Count;
        }

        var relativePoint = e.GetPosition(dropTarget);
        return relativePoint.X >= dropTarget.ActualWidth * 0.5 ? index + 1 : index;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T found)
            {
                return found;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
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
        if (!HasFrames)
        {
            StopPlayback();
            return;
        }

        if (IsLoopEnabled)
        {
            MoveSelection(1, wrap: true);
            return;
        }

        if (SelectedFrameIndex >= Frames.Count - 1)
        {
            StopPlayback();
            return;
        }

        MoveSelection(1, wrap: false);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.X)
        {
            CropCurrentCanvas();
            e.Handled = true;
            return;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.S)
        {
            TrySaveProject(forceSaveAs: true);
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control && key == Key.N)
        {
            CreateNewProject();
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control && key == Key.O)
        {
            OpenProject();
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control && key == Key.S)
        {
            TrySaveProject(forceSaveAs: false);
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control
            && key == Key.V
            && (ThumbnailList.IsKeyboardFocusWithin || ThumbnailList.IsMouseOver)
            && PasteClipboardImagesAfterSelection())
        {
            e.Handled = true;
            return;
        }

        if (PreviewOverlayCanvas.IsKeyboardFocusWithin && TryMoveCurrentFrameByArrowKey(key, modifiers))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left)
        {
            MoveSelection(-1, wrap: IsLoopEnabled);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right)
        {
            MoveSelection(1, wrap: IsLoopEnabled);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            TogglePlayback();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            RemoveSelectedFrame();
            e.Handled = true;
        }
    }

    private bool PasteClipboardImagesAfterSelection()
    {
        var imagesToInsert = new List<(BitmapSource Image, string? Name, string? SourcePath)>();

        try
        {
            if (Clipboard.ContainsImage())
            {
                var clipboardImage = Clipboard.GetImage();
                if (clipboardImage is not null)
                {
                    imagesToInsert.Add((clipboardImage, null, null));
                }
            }
            else if (Clipboard.ContainsFileDropList())
            {
                var droppedFiles = Clipboard.GetFileDropList();
                foreach (var path in droppedFiles)
                {
                    if (path is null)
                    {
                        continue;
                    }

                    if (!IsSupportedImagePath(path))
                    {
                        continue;
                    }

                    try
                    {
                        imagesToInsert.Add((LoadBitmap(path), Path.GetFileName(path), path));
                    }
                    catch
                    {
                        // Continue importing valid files if one file fails.
                    }
                }
            }
        }
        catch
        {
            return false;
        }

        if (imagesToInsert.Count == 0)
        {
            return false;
        }

        var insertionIndex = SelectedFrameIndex >= 0 ? SelectedFrameIndex + 1 : Frames.Count;
        var lastInsertedIndex = -1;

        foreach (var (image, name, sourcePath) in imagesToInsert)
        {
            lastInsertedIndex = InsertFrameAtIndex(insertionIndex, image, name, sourcePath);
            insertionIndex = lastInsertedIndex + 1;
        }

        if (lastInsertedIndex >= 0)
        {
            SelectedFrameIndex = lastInsertedIndex;
        }

        return true;
    }

    private void NewProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CreateNewProject();
    }

    private void OpenProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenProject();
    }

    private void SaveProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TrySaveProject(forceSaveAs: false);
    }

    private void SaveProjectAsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TrySaveProject(forceSaveAs: true);
    }

    private void CropMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CropCurrentCanvas();
    }

    private void OptionsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var optionsWindow = new OptionsWindow
        {
            Owner = this
        };
        optionsWindow.ShowDialog();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CreateNewProject()
    {
        if (!TryConfirmDiscardUnsavedChanges())
        {
            return;
        }

        _suppressDirtyTracking = true;
        try
        {
            StopPlayback();
            ClearPreviewSelection();
            Frames.Clear();
            ResetCanvas();
            IsLoopEnabled = false;
            SelectedFrameIndex = -1;
            OnPropertyChanged(nameof(HasFrames));
            OnPropertyChanged(nameof(CurrentFrameImage));
            OnPropertyChanged(nameof(CurrentFrameSummary));
        }
        finally
        {
            _suppressDirtyTracking = false;
        }

        SetCurrentProjectPath(null);
        MarkClean();
    }

    private void OpenProject()
    {
        if (!TryConfirmDiscardUnsavedChanges())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = ProjectFileFilter,
            DefaultExt = ".ffproj"
        };

        if (dialog.ShowDialog(this) == true)
        {
            OpenProjectFromPath(dialog.FileName, confirmUnsavedChanges: false);
        }
    }

    public bool OpenProjectFromPath(string projectPath, bool confirmUnsavedChanges = true)
    {
        if (confirmUnsavedChanges && !TryConfirmDiscardUnsavedChanges())
        {
            return false;
        }

        return TryLoadProject(projectPath);
    }

    private bool TryLoadProject(string projectPath)
    {
        try
        {
            var loadResult = ProjectStorageService.LoadProject(projectPath);

            _suppressDirtyTracking = true;
            try
            {
                StopPlayback();
                ClearPreviewSelection();
                Frames.Clear();
                SetCanvasSize(loadResult.CanvasWidth, loadResult.CanvasHeight);
                foreach (var frame in loadResult.Frames)
                {
                    Frames.Add(frame);
                }

                IsLoopEnabled = loadResult.IsLoopEnabled;
                SelectedFrameIndex = loadResult.SelectedFrameIndex;

                OnPropertyChanged(nameof(HasFrames));
                OnPropertyChanged(nameof(CurrentFrameImage));
                OnPropertyChanged(nameof(CurrentFrameSummary));
                UpdatePreviewWorkspaceLayout();
            }
            finally
            {
                _suppressDirtyTracking = false;
            }

            SetCurrentProjectPath(projectPath);
            MarkClean();

            if (loadResult.MissingFrameCount > 0)
            {
                MessageBox.Show(
                    this,
                    $"프로젝트를 열었지만 {loadResult.MissingFrameCount}개의 프레임 파일을 찾지 못해 건너뛰었습니다.",
                    "프로젝트 열기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"프로젝트를 열 수 없습니다.\n{ex.Message}",
                "프로젝트 열기",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private bool TrySaveProject(bool forceSaveAs)
    {
        var targetProjectPath = _currentProjectPath;
        if (forceSaveAs || string.IsNullOrWhiteSpace(targetProjectPath))
        {
            var dialog = new SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = ".ffproj",
                Filter = ProjectFileFilter,
                OverwritePrompt = true,
                FileName = string.IsNullOrWhiteSpace(targetProjectPath)
                    ? "project.ffproj"
                    : Path.GetFileName(targetProjectPath)
            };

            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            targetProjectPath = dialog.FileName;
        }

        try
        {
            ProjectStorageService.SaveProject(
                targetProjectPath!,
                Frames,
                CanvasWidth,
                CanvasHeight,
                IsLoopEnabled,
                SelectedFrameIndex);
            SetCurrentProjectPath(targetProjectPath);
            MarkClean();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"프로젝트를 저장할 수 없습니다.\n{ex.Message}",
                "프로젝트 저장",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private bool TryConfirmDiscardUnsavedChanges()
    {
        if (!_isDirty)
        {
            return true;
        }

        var result = MessageBox.Show(
            this,
            "저장되지 않은 변경 사항이 있습니다. 저장하시겠습니까?",
            "FrameForge",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);

        if (result == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (result == MessageBoxResult.Yes)
        {
            return TrySaveProject(forceSaveAs: false);
        }

        return true;
    }

    private void PreviousFrameButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelection(-1, wrap: IsLoopEnabled);
    }

    private void NextFrameButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelection(1, wrap: IsLoopEnabled);
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

    private void PreviewOverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!HasCanvas
            || _previewLayout.CanvasRect.IsEmpty
            || PreviewOverlayCanvas.ActualWidth <= 0
            || PreviewOverlayCanvas.ActualHeight <= 0)
        {
            return;
        }

        StopPlayback();
        Keyboard.Focus(PreviewOverlayCanvas);

        var startPoint = e.GetPosition(PreviewOverlayCanvas);
        if (!_previewLayout.CanvasRect.Contains(startPoint))
        {
            return;
        }

        _isPreviewDragging = true;
        _previewDragStartPoint = ClampPointToRect(startPoint, _previewLayout.CanvasRect);
        _previewSelectionDip = new Rect(_previewDragStartPoint, _previewDragStartPoint);
        PreviewOverlayCanvas.CaptureMouse();
        UpdatePreviewSelectionVisual();
        e.Handled = true;
    }

    private void PreviewOverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPreviewDragging || !HasCanvas || _previewLayout.CanvasRect.IsEmpty)
        {
            return;
        }

        var currentPoint = ClampPointToRect(e.GetPosition(PreviewOverlayCanvas), _previewLayout.CanvasRect);
        _previewSelectionDip = ImageSelectionHelper.NormalizeRect(_previewDragStartPoint, currentPoint);
        UpdatePreviewSelectionVisual();
        e.Handled = true;
    }

    private void PreviewOverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPreviewDragging)
        {
            return;
        }

        _isPreviewDragging = false;
        PreviewOverlayCanvas.ReleaseMouseCapture();

        var currentPoint = _previewLayout.CanvasRect.IsEmpty
            ? e.GetPosition(PreviewOverlayCanvas)
            : ClampPointToRect(e.GetPosition(PreviewOverlayCanvas), _previewLayout.CanvasRect);
        _previewSelectionDip = ImageSelectionHelper.NormalizeRect(_previewDragStartPoint, currentPoint);

        if (!HasValidPreviewSelection(_previewSelectionDip))
        {
            ClearPreviewSelection();
        }
        else
        {
            UpdatePreviewSelectionVisual();
        }

        e.Handled = true;
    }

    private void ThumbnailList_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(FrameDragDataFormat))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void ThumbnailList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(FrameDragDataFormat)
            && e.Data.GetData(FrameDragDataFormat) is AnimationFrame draggedFrame)
        {
            var sourceIndex = Frames.IndexOf(draggedFrame);
            var insertionIndex = GetThumbnailInsertionIndexFromDropPosition(e);
            MoveFrameByInsertionIndex(sourceIndex, insertionIndex);
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] droppedFiles)
        {
            AddFramesFromPaths(droppedFiles);
            e.Handled = true;
        }
    }

    private void ThumbnailList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ThumbnailList.IsKeyboardFocusWithin)
        {
            Keyboard.Focus(ThumbnailList);
        }

        _thumbnailDragStartPoint = e.GetPosition(ThumbnailList);

        var clickedItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        _thumbnailDragSourceFrame = clickedItem?.DataContext as AnimationFrame;

        if (_thumbnailDragSourceFrame is not null)
        {
            ThumbnailList.SelectedItem = _thumbnailDragSourceFrame;
        }
    }

    private void ThumbnailList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _thumbnailDragSourceFrame is null)
        {
            return;
        }

        var currentPoint = e.GetPosition(ThumbnailList);
        var delta = _thumbnailDragStartPoint - currentPoint;

        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragData = new DataObject();
        dragData.SetData(FrameDragDataFormat, _thumbnailDragSourceFrame);

        DragDrop.DoDragDrop(ThumbnailList, dragData, DragDropEffects.Move);
        _thumbnailDragSourceFrame = null;
    }

    private void ThumbnailList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ThumbnailHeight = Math.Max(48, e.NewSize.Height - 36);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!TryConfirmDiscardUnsavedChanges())
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
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
    public AnimationFrame(
        string name,
        BitmapSource image,
        int x,
        int y,
        string? sourcePath = null,
        string? assetId = null,
        string? assetPath = null)
    {
        Name = name;
        X = x;
        Y = y;
        SourcePath = sourcePath;
        AssetId = assetId;
        AssetPath = assetPath;
        Image = image;
    }

    public string Name { get; }
    public int X { get; }
    public int Y { get; }
    public string? SourcePath { get; }
    public string? AssetId { get; }
    public string? AssetPath { get; }
    public BitmapSource Image { get; }

    public AnimationFrame WithPosition(int x, int y)
    {
        return new AnimationFrame(Name, Image, x, y, SourcePath, AssetId, AssetPath);
    }
}

