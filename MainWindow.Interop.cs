using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WorkAudit;

/// <summary>Win32 hooks so borderless/chromeless windows maximize to the monitor work area (not under the taskbar).</summary>
public partial class MainWindow
{
    private const int WmGetMinMaxInfoMsg = 0x0024;

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        if (HwndSource.FromHwnd(handle) is { } source)
            source.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfoMsg)
            ApplyMonitorWorkAreaToMinMax(hwnd, lParam);
        return IntPtr.Zero;
    }

    private static void ApplyMonitorWorkAreaToMinMax(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);

        const uint monitorDefaultToNearest = 2;
        var monitor = NativeMethods.MonitorFromWindow(hwnd, monitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return;

        var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
            return;

        var work = monitorInfo.Work;
        var monitorRect = monitorInfo.Monitor;

        mmi.MaxPosition.X = Math.Abs(work.Left - monitorRect.Left);
        mmi.MaxPosition.Y = Math.Abs(work.Top - monitorRect.Top);
        mmi.MaxSize.X = Math.Abs(work.Right - work.Left);
        mmi.MaxSize.Y = Math.Abs(work.Bottom - work.Top);

        Marshal.StructureToPtr(mmi, lParam, false);
    }

    /// <summary>Size and position the window to the full physical monitor (true fullscreen, including over the taskbar).</summary>
    private void ApplyFullMonitorBounds()
    {
        var helper = new WindowInteropHelper(this);
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero) return;
        if (!TryGetMonitorRectPixels(hwnd, out var left, out var top, out var right, out var bottom))
            return;
        if (HwndSource.FromHwnd(hwnd) is not { CompositionTarget: { } ct })
            return;

        var fromDevice = ct.TransformFromDevice;
        var topLeft = fromDevice.Transform(new System.Windows.Point(left, top));
        var bottomRight = fromDevice.Transform(new System.Windows.Point(right, bottom));

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = Math.Max(1, bottomRight.X - topLeft.X);
        Height = Math.Max(1, bottomRight.Y - topLeft.Y);
    }

    private static bool TryGetMonitorRectPixels(IntPtr hwnd, out int left, out int top, out int right, out int bottom)
    {
        left = top = right = bottom = 0;
        const uint monitorDefaultToNearest = 2;
        var monitor = NativeMethods.MonitorFromWindow(hwnd, monitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return false;
        var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
            return false;
        left = monitorInfo.Monitor.Left;
        top = monitorInfo.Monitor.Top;
        right = monitorInfo.Monitor.Right;
        bottom = monitorInfo.Monitor.Bottom;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);
    }
}
