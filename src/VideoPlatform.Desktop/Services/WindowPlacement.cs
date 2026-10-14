using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Forms;
using VideoPlatform.Desktop.Models;

namespace VideoPlatform.Desktop.Services;

public static class WindowPlacement
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePlacement
    {
        public int Length, Flags, Show;
        public System.Drawing.Point Min, Max;
        public NativeRect Normal;
    }
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr handle, ref NativePlacement placement);

    public static WorkspacePlacement? Capture(Window? window)
    {
        if (window is null) return null;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return null;
        var screen = Screen.FromHandle(handle);
        var scale = Math.Max(96, GetDpiForWindow(handle)) / 96d;
        if (window.WindowState != WindowState.Normal)
        {
            var placement = new NativePlacement { Length = Marshal.SizeOf<NativePlacement>() };
            if (GetWindowPlacement(handle, ref placement))
            {
                rect = placement.Normal;
                rect.Left += screen.WorkingArea.Left - screen.Bounds.Left;
                rect.Top += screen.WorkingArea.Top - screen.Bounds.Top;
                rect.Right += screen.WorkingArea.Left - screen.Bounds.Left;
                rect.Bottom += screen.WorkingArea.Top - screen.Bounds.Top;
            }
        }
        return new(screen.DeviceName, rect.Left, rect.Top, (rect.Right - rect.Left) / scale,
            (rect.Bottom - rect.Top) / scale, window.WindowState == WindowState.Maximized);
    }

    public static void Restore(Window window, WorkspacePlacement? saved)
    {
        if (saved is null) { EnsureVisible(window); return; }
        if (!double.IsFinite(saved.Width) || !double.IsFinite(saved.Height) || !double.IsFinite(saved.Left) || !double.IsFinite(saved.Top)) return;
        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == saved.Display) ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
        window.WindowState = WindowState.Normal;
        window.Width = Math.Clamp(saved.Width, window.MinWidth, 4000);
        window.Height = Math.Clamp(saved.Height, window.MinHeight, 2400);
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var area = screen.WorkingArea;
        var x = (int)Math.Clamp(saved.Left, area.Left, Math.Max(area.Left, area.Right - 200));
        var y = (int)Math.Clamp(saved.Top, area.Top, Math.Max(area.Top, area.Bottom - 100));
        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0015);
        var scale = Math.Max(96, GetDpiForWindow(handle)) / 96d;
        var width = Math.Min(area.Width, (int)(window.Width * scale));
        var height = Math.Min(area.Height, (int)(window.Height * scale));
        SetWindowPos(handle, IntPtr.Zero, Math.Clamp(x, area.Left, area.Right - width), Math.Clamp(y, area.Top, area.Bottom - height), width, height, 0x0014);
        if (saved.Maximized) window.WindowState = WindowState.Maximized;
    }

    public static void EnsureVisible(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return;
        if (Screen.AllScreens.Any(screen => rect.Left < screen.WorkingArea.Right - 100 && rect.Right > screen.WorkingArea.Left + 100
            && rect.Top < screen.WorkingArea.Bottom - 50 && rect.Bottom > screen.WorkingArea.Top + 50)) return;
        var area = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
        SetWindowPos(handle, IntPtr.Zero, area.Left, area.Top, Math.Min(rect.Right - rect.Left, area.Width), Math.Min(rect.Bottom - rect.Top, area.Height), 0x0014);
    }
}
