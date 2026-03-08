using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WpfDevTools.Tests.TestApp;

internal static class WindowBackdropHelper
{
    private const int DwMwaSystemBackdropType = 38;
    private const int DwMwaMicaEffect = 1029;
    private const int DwmSystemBackdropMainWindow = 2;

    public static bool TryApplyMica(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var backdropType = DwmSystemBackdropMainWindow;
        if (DwmSetWindowAttribute(hwnd, DwMwaSystemBackdropType, ref backdropType, sizeof(int)) == 0)
        {
            return true;
        }

        var enabled = 1;
        return DwmSetWindowAttribute(hwnd, DwMwaMicaEffect, ref enabled, sizeof(int)) == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int attributeSize);
}
