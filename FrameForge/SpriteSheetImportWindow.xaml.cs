using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FrameForge;

public partial class SpriteSheetImportWindow : Window
{
    private readonly Action<BitmapSource> _onFrameCaptured;
    private BitmapSource? _sourceImage;
    private bool _isDragging;
    private Point _dragStart;
    private Rect _selectionDip;

    public SpriteSheetImportWindow(Action<BitmapSource> onFrameCaptured)
    {
        InitializeComponent();
        _onFrameCaptured = onFrameCaptured;
    }

    private void LoadImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = false,
            CheckFileExists = true,
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All Files|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        LoadSourceImage(LoadBitmap(dialog.FileName));
    }

    private void LoadFromClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        TryLoadFromClipboard(showMessageOnFailure: true);
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

    private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_sourceImage is null)
        {
            return;
        }

        Focus();
        _isDragging = true;
        _dragStart = e.GetPosition(OverlayCanvas);
        _selectionDip = new Rect(_dragStart, _dragStart);
        OverlayCanvas.CaptureMouse();
        UpdateSelectionVisual(showHint: false);
        e.Handled = true;
    }

    private void OverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _sourceImage is null)
        {
            return;
        }

        var current = e.GetPosition(OverlayCanvas);
        _selectionDip = NormalizeRect(_dragStart, current);
        UpdateSelectionVisual(showHint: true);
        e.Handled = true;
    }

    private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        OverlayCanvas.ReleaseMouseCapture();

        var current = e.GetPosition(OverlayCanvas);
        _selectionDip = NormalizeRect(_dragStart, current);

        if (!HasSelection())
        {
            ClearSelection();
        }
        else
        {
            UpdateSelectionVisual(showHint: true);
        }

        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            TryLoadFromClipboard(showMessageOnFailure: true);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Space)
        {
            return;
        }

        TryCaptureSelection();
        e.Handled = true;
    }

    private bool TryCaptureSelection()
    {
        if (_sourceImage is null || !HasSelection())
        {
            return false;
        }

        var pixelRect = ToPixelRect(_selectionDip, _sourceImage);
        if (pixelRect.IsEmpty || pixelRect.Width <= 0 || pixelRect.Height <= 0)
        {
            return false;
        }

        var cropped = new CroppedBitmap(_sourceImage, pixelRect);
        cropped.Freeze();
        _onFrameCaptured(cropped);
        return true;
    }

    private bool HasSelection()
    {
        return _selectionDip.Width >= 2 && _selectionDip.Height >= 2;
    }

    private static Rect NormalizeRect(Point start, Point end)
    {
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        var width = Math.Abs(end.X - start.X);
        var height = Math.Abs(end.Y - start.Y);
        return new Rect(x, y, width, height);
    }

    private static Int32Rect ToPixelRect(Rect dipRect, BitmapSource source)
    {
        var scaleX = source.PixelWidth / Math.Max(1.0, source.Width);
        var scaleY = source.PixelHeight / Math.Max(1.0, source.Height);

        var x = (int)Math.Floor(dipRect.X * scaleX);
        var y = (int)Math.Floor(dipRect.Y * scaleY);
        var width = Math.Max(1, (int)Math.Ceiling(dipRect.Width * scaleX));
        var height = Math.Max(1, (int)Math.Ceiling(dipRect.Height * scaleY));

        var left = Math.Clamp(x, 0, source.PixelWidth);
        var top = Math.Clamp(y, 0, source.PixelHeight);
        var right = Math.Clamp(x + width, 0, source.PixelWidth);
        var bottom = Math.Clamp(y + height, 0, source.PixelHeight);

        var clippedWidth = Math.Max(0, right - left);
        var clippedHeight = Math.Max(0, bottom - top);

        if (clippedWidth == 0 || clippedHeight == 0)
        {
            return Int32Rect.Empty;
        }

        return new Int32Rect(left, top, clippedWidth, clippedHeight);
    }

    private void UpdateSelectionVisual(bool showHint)
    {
        if (!HasSelection())
        {
            SelectionRectangle.Visibility = Visibility.Collapsed;
            SelectionHint.Visibility = Visibility.Collapsed;
            return;
        }

        SelectionRectangle.Visibility = Visibility.Visible;
        SelectionRectangle.Width = _selectionDip.Width;
        SelectionRectangle.Height = _selectionDip.Height;
        Canvas.SetLeft(SelectionRectangle, _selectionDip.X);
        Canvas.SetTop(SelectionRectangle, _selectionDip.Y);

        if (!showHint)
        {
            SelectionHint.Visibility = Visibility.Collapsed;
            return;
        }

        SelectionHint.Visibility = Visibility.Visible;
        SelectionHint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var hintWidth = SelectionHint.DesiredSize.Width;
        var hintHeight = SelectionHint.DesiredSize.Height;

        var maxLeft = Math.Max(0, OverlayCanvas.ActualWidth - hintWidth);
        var left = Math.Max(0, Math.Min(_selectionDip.X, maxLeft));
        var top = Math.Max(0, _selectionDip.Y - hintHeight - 6);

        Canvas.SetLeft(SelectionHint, left);
        Canvas.SetTop(SelectionHint, top);
    }

    private void ClearSelection()
    {
        _selectionDip = Rect.Empty;
        SelectionRectangle.Visibility = Visibility.Collapsed;
        SelectionHint.Visibility = Visibility.Collapsed;
    }

    private void LoadSourceImage(BitmapSource image)
    {
        if (image is Freezable freezable && freezable.CanFreeze && !freezable.IsFrozen)
        {
            freezable.Freeze();
        }

        _sourceImage = image;
        SourceImageView.Source = _sourceImage;

        ImageScrollViewer.Visibility = Visibility.Visible;
        LoadPanel.Visibility = Visibility.Collapsed;
        ClearSelection();
        Keyboard.Focus(this);
    }

    private void TryLoadFromClipboard(bool showMessageOnFailure)
    {
        try
        {
            if (!Clipboard.ContainsImage())
            {
                if (showMessageOnFailure)
                {
                    MessageBox.Show(this, "클립보드에 이미지가 없습니다.", "클립보드 불러오기", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return;
            }

            var clipboardImage = Clipboard.GetImage();
            if (clipboardImage is null)
            {
                if (showMessageOnFailure)
                {
                    MessageBox.Show(this, "클립보드 이미지를 읽을 수 없습니다.", "클립보드 불러오기", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                return;
            }

            var copied = new WriteableBitmap(clipboardImage);
            copied.Freeze();
            LoadSourceImage(copied);
        }
        catch
        {
            if (showMessageOnFailure)
            {
                MessageBox.Show(this, "클립보드 접근 중 오류가 발생했습니다.", "클립보드 불러오기", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
