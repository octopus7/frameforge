using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FrameForge;

public partial class VideoImportWindow : Window, INotifyPropertyChanged
{
    private readonly Action<VideoImportResult> _onImportConfirmed;
    private CancellationTokenSource? _loadCancellation;
    private Point _previewPointerDown;
    private bool _previewPointerPressed;
    private bool _previewPendingClearOnMouseUp;
    private bool _isPreviewDragging;
    private int? _pendingClickedFrameIndex;
    private ModifierKeys _pendingClickedModifiers;
    private bool _suppressSelectionSync;
    private bool _isDisposed;
    private string _loadProgressText = "동영상 파일을 선택하세요.";

    public VideoImportWindow(Action<VideoImportResult> onImportConfirmed)
    {
        InitializeComponent();
        _onImportConfirmed = onImportConfirmed;
        Session = new VideoImportSession();
        Session.PropertyChanged += Session_PropertyChanged;
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public VideoImportSession Session { get; }

    public string LoadProgressText
    {
        get => _loadProgressText;
        private set
        {
            if (string.Equals(_loadProgressText, value, StringComparison.Ordinal))
            {
                return;
            }

            _loadProgressText = value;
            OnPropertyChanged();
        }
    }

    public bool HasSelectedFrames => FrameList?.SelectedItems.Count > 0;

    public bool CanDeleteFrames => !Session.IsLoading && HasSelectedFrames;

    public bool CanCropFrames => !Session.IsLoading && Session.HasFrames && Session.HasCropSelection;

    public bool CanConfirmImport => !Session.IsLoading && Session.HasFrames;

    public string ActiveFrameSummary =>
        Session.ActiveFrameItem is null
            ? "액티브 프레임이 없습니다."
            : $"{Session.ActiveFrameItem.DisplayName} | {Session.ActiveFrameItem.Subtitle}";

    private async void LoadVideoButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Multiselect = false,
            Filter = "Video Files|*.mp4;*.mov;*.avi;*.mkv;*.webm;*.wmv;*.m4v;*.mpeg;*.mpg|All Files|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!await VideoRuntimeSetupCoordinator.EnsureRuntimeAvailableAsync(this))
        {
            return;
        }

        await LoadVideoAsync(dialog.FileName);
    }

    private async Task LoadVideoAsync(string videoPath)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();

        Session.BeginLoading(videoPath);
        LoadProgressText = "프레임을 추출하는 중입니다...";
        ClearListSelection();
        UpdateCropSelectionVisual();
        RefreshDerivedState();

        try
        {
            var progress = new Progress<VideoFrameCaptureProgress>(UpdateLoadProgress);
            var capturedFrames = await Task.Run(
                () => VideoFrameCaptureService.CaptureFrames(videoPath, progress, _loadCancellation.Token),
                _loadCancellation.Token);

            if (_isDisposed)
            {
                return;
            }

            Session.CompleteLoading(capturedFrames);

            if (Session.HasFrames)
            {
                LoadProgressText = $"총 {Session.Frames.Count}개 프레임을 불러왔습니다.";
                SelectActiveFrame();
            }
            else
            {
                LoadProgressText = "프레임을 찾지 못했습니다.";
            }
        }
        catch (OperationCanceledException)
        {
            if (!_isDisposed)
            {
                LoadProgressText = "동영상 파일을 선택하세요.";
            }
        }
        catch (Exception ex)
        {
            if (_isDisposed)
            {
                return;
            }

            Session.FailLoading($"동영상을 불러올 수 없습니다.\n{ex.Message}");
            LoadProgressText = "동영상 파일을 선택하세요.";
            MessageBox.Show(this, Session.LoadError, "동영상 불러오기", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshDerivedState();
            UpdateCropSelectionVisual();
        }
    }

    private void UpdateLoadProgress(VideoFrameCaptureProgress progress)
    {
        LoadProgressText = progress.TotalFrameCount is long totalFrameCount && totalFrameCount > 0
            ? $"프레임 추출 중... {progress.DecodedFrameCount} / {totalFrameCount}"
            : $"프레임 추출 중... {progress.DecodedFrameCount}";
    }

    private void FrameList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!FrameList.IsKeyboardFocusWithin)
        {
            Keyboard.Focus(FrameList);
        }

        var clickedItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (clickedItem is null)
        {
            _pendingClickedFrameIndex = null;
            return;
        }

        var clickedIndex = FrameList.ItemContainerGenerator.IndexFromContainer(clickedItem);
        if (clickedIndex < 0)
        {
            _pendingClickedFrameIndex = null;
            return;
        }

        _pendingClickedFrameIndex = clickedIndex;
        _pendingClickedModifiers = Keyboard.Modifiers;
    }

    private void FrameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionSync)
        {
            RefreshDerivedState();
            return;
        }

        var selectedIndices = GetSelectedIndices();
        if (_pendingClickedFrameIndex is int clickedIndex)
        {
            Session.ApplySelectionInteraction(clickedIndex, _pendingClickedModifiers, selectedIndices);
        }
        else
        {
            Session.SyncActiveFrame(selectedIndices);
        }

        _pendingClickedFrameIndex = null;
        RefreshDerivedState();
    }

    private void DeleteFramesButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedFrames();
    }

    private void CropFramesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCropFrames || Session.ActiveFrameImage is null)
        {
            return;
        }

        var pixelRect = ToPixelRect(Session.CropSelectionDip, Session.ActiveFrameImage, ActiveFrameImageView);
        if (pixelRect.IsEmpty)
        {
            return;
        }

        Session.ApplyCrop(pixelRect);
        UpdateCropSelectionVisual();
        RefreshDerivedState();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanConfirmImport)
        {
            return;
        }

        _onImportConfirmed(Session.CreateResult());
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PreviewOverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Session.ActiveFrameImage is null)
        {
            return;
        }

        var point = ClampPreviewPoint(e.GetPosition(PreviewOverlayCanvas));
        if (!GetPreviewBounds().Contains(point))
        {
            return;
        }

        Focus();
        _previewPointerPressed = true;
        _previewPendingClearOnMouseUp = Session.HasCropSelection && !Session.CropSelectionDip.Contains(point);
        _previewPointerDown = point;
        _isPreviewDragging = false;
        PreviewOverlayCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void PreviewOverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_previewPointerPressed || Session.ActiveFrameImage is null)
        {
            return;
        }

        var currentPoint = ClampPreviewPoint(e.GetPosition(PreviewOverlayCanvas));
        if (!_isPreviewDragging)
        {
            if (!HasDragThreshold(_previewPointerDown, currentPoint))
            {
                return;
            }

            _isPreviewDragging = true;
        }

        Session.SetCropSelection(ImageSelectionHelper.NormalizeRect(_previewPointerDown, currentPoint));
        UpdateCropSelectionVisual();
        RefreshDerivedState();
        e.Handled = true;
    }

    private void PreviewOverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_previewPointerPressed)
        {
            return;
        }

        var currentPoint = ClampPreviewPoint(e.GetPosition(PreviewOverlayCanvas));
        if (_isPreviewDragging)
        {
            Session.SetCropSelection(ImageSelectionHelper.NormalizeRect(_previewPointerDown, currentPoint));
        }
        else if (_previewPendingClearOnMouseUp)
        {
            Session.ClearCropSelection();
        }

        ResetPreviewPointerState();
        UpdateCropSelectionVisual();
        RefreshDerivedState();
        e.Handled = true;
    }

    private void PreviewOverlayCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        ResetPreviewPointerState();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && DeleteSelectedFrames())
        {
            e.Handled = true;
        }
    }

    private bool DeleteSelectedFrames()
    {
        var selectedIndices = GetSelectedIndices();
        if (!Session.RemoveFrames(selectedIndices))
        {
            return false;
        }

        SelectActiveFrame();
        UpdateCropSelectionVisual();
        RefreshDerivedState();
        return true;
    }

    private void SelectActiveFrame()
    {
        _suppressSelectionSync = true;
        try
        {
            FrameList.UnselectAll();
            if (Session.ActiveFrameIndex >= 0)
            {
                FrameList.SelectedIndex = Session.ActiveFrameIndex;
                if (FrameList.SelectedItem is object selectedItem)
                {
                    FrameList.ScrollIntoView(selectedItem);
                }
            }
        }
        finally
        {
            _suppressSelectionSync = false;
        }
    }

    private void ClearListSelection()
    {
        _suppressSelectionSync = true;
        try
        {
            FrameList.UnselectAll();
        }
        finally
        {
            _suppressSelectionSync = false;
        }
    }

    private List<int> GetSelectedIndices()
    {
        return FrameList.SelectedItems
            .Cast<VideoImportFrameItem>()
            .Select(frame => Session.Frames.IndexOf(frame))
            .Where(index => index >= 0)
            .OrderBy(index => index)
            .ToList();
    }

    private void UpdateCropSelectionVisual()
    {
        if (!Session.HasCropSelection)
        {
            CropSelectionShadow.Visibility = Visibility.Collapsed;
            CropSelectionHighlight.Visibility = Visibility.Collapsed;
            return;
        }

        var selection = Session.CropSelectionDip;
        UpdateSelectionRectangle(CropSelectionShadow, selection);
        UpdateSelectionRectangle(CropSelectionHighlight, selection);
        CropSelectionShadow.Visibility = Visibility.Visible;
        CropSelectionHighlight.Visibility = Visibility.Visible;
    }

    private static void UpdateSelectionRectangle(FrameworkElement element, Rect selection)
    {
        element.Width = selection.Width;
        element.Height = selection.Height;
        Canvas.SetLeft(element, selection.X);
        Canvas.SetTop(element, selection.Y);
    }

    private Point ClampPreviewPoint(Point point)
    {
        var bounds = GetPreviewBounds();
        return new Point(
            Math.Clamp(point.X, bounds.Left, bounds.Right),
            Math.Clamp(point.Y, bounds.Top, bounds.Bottom));
    }

    private Rect GetPreviewBounds()
    {
        return new Rect(0, 0, PreviewOverlayCanvas.ActualWidth, PreviewOverlayCanvas.ActualHeight);
    }

    private static bool HasDragThreshold(Point origin, Point current)
    {
        return Math.Abs(current.X - origin.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(current.Y - origin.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private static Int32Rect ToPixelRect(Rect selectionDip, BitmapSource image, FrameworkElement imageView)
    {
        if (selectionDip.Width <= 0 || selectionDip.Height <= 0 || imageView.ActualWidth <= 0 || imageView.ActualHeight <= 0)
        {
            return Int32Rect.Empty;
        }

        var scaleX = image.PixelWidth / imageView.ActualWidth;
        var scaleY = image.PixelHeight / imageView.ActualHeight;

        var x = (int)Math.Floor(selectionDip.X * scaleX);
        var y = (int)Math.Floor(selectionDip.Y * scaleY);
        var right = (int)Math.Ceiling(selectionDip.Right * scaleX);
        var bottom = (int)Math.Ceiling(selectionDip.Bottom * scaleY);

        x = Math.Clamp(x, 0, image.PixelWidth - 1);
        y = Math.Clamp(y, 0, image.PixelHeight - 1);
        right = Math.Clamp(right, x + 1, image.PixelWidth);
        bottom = Math.Clamp(bottom, y + 1, image.PixelHeight);

        return new Int32Rect(x, y, right - x, bottom - y);
    }

    private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VideoImportSession.ActiveFrameImage)
            or nameof(VideoImportSession.ActiveFrameIndex)
            or nameof(VideoImportSession.HasCropSelection)
            or nameof(VideoImportSession.HasFrames)
            or nameof(VideoImportSession.IsLoading))
        {
            RefreshDerivedState();
        }
    }

    private void RefreshDerivedState()
    {
        OnPropertyChanged(nameof(HasSelectedFrames));
        OnPropertyChanged(nameof(CanDeleteFrames));
        OnPropertyChanged(nameof(CanCropFrames));
        OnPropertyChanged(nameof(CanConfirmImport));
        OnPropertyChanged(nameof(ActiveFrameSummary));
    }

    private void ResetPreviewPointerState()
    {
        _previewPointerPressed = false;
        _previewPendingClearOnMouseUp = false;
        _isPreviewDragging = false;

        if (PreviewOverlayCanvas.IsMouseCaptured)
        {
            PreviewOverlayCanvas.ReleaseMouseCapture();
        }
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

    protected override void OnClosed(EventArgs e)
    {
        _isDisposed = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        Session.PropertyChanged -= Session_PropertyChanged;
        base.OnClosed(e);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
