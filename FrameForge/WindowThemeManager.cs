using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FrameForge;

public static class WindowThemeManager
{
    private const int DwmaUseImmersiveDarkMode = 20;
    private const int DwmaUseImmersiveDarkModeLegacy = 19;
    private const int DwmaCaptionColor = 35;
    private const int DwmaTextColor = 36;

    public static readonly DependencyProperty EnableDarkCaptionProperty =
        DependencyProperty.RegisterAttached(
            "EnableDarkCaption",
            typeof(bool),
            typeof(WindowThemeManager),
            new PropertyMetadata(false, OnEnableDarkCaptionChanged));

    public static void SetEnableDarkCaption(DependencyObject element, bool value)
    {
        element.SetValue(EnableDarkCaptionProperty, value);
    }

    public static bool GetEnableDarkCaption(DependencyObject element)
    {
        return (bool)element.GetValue(EnableDarkCaptionProperty);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private static void OnEnableDarkCaptionChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not Window window)
        {
            return;
        }

        if (eventArgs.NewValue is true)
        {
            window.SourceInitialized += Window_SourceInitialized;
            if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            {
                ApplyDarkCaption(window);
            }
        }
        else
        {
            window.SourceInitialized -= Window_SourceInitialized;
        }
    }

    private static void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            ApplyDarkCaption(window);
        }
    }

    private static void ApplyDarkCaption(Window window)
    {
        var windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        var enabled = 1;
        var primaryResult = DwmSetWindowAttribute(windowHandle, DwmaUseImmersiveDarkMode, ref enabled, sizeof(int));
        if (primaryResult != 0)
        {
            DwmSetWindowAttribute(windowHandle, DwmaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
        }

        var darkCaptionColor = ToColorRef(red: 0x1A, green: 0x1A, blue: 0x1A);
        DwmSetWindowAttribute(windowHandle, DwmaCaptionColor, ref darkCaptionColor, sizeof(int));

        var lightTextColor = ToColorRef(red: 0xF2, green: 0xF2, blue: 0xF2);
        DwmSetWindowAttribute(windowHandle, DwmaTextColor, ref lightTextColor, sizeof(int));
    }

    private static int ToColorRef(byte red, byte green, byte blue)
    {
        return red | (green << 8) | (blue << 16);
    }
}
