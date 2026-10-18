using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using CodexQuotaBar.Core;

namespace CodexQuotaBar;

internal sealed record WindowIdentity(long Handle, uint ProcessId, string Executable, string Package, string ClassName,
    PixelRect Bounds, uint Dpi, bool Visible, bool Minimized, bool Cloaked);

internal readonly record struct PointerSample(bool Pressed, int X, int Y, bool Valid = true);

internal static class NativeWindows
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    internal static object Displays() => new { monitorCount = GetSystemMetrics(80), primaryWidth = GetSystemMetrics(0), primaryHeight = GetSystemMetrics(1), virtualWidth = GetSystemMetrics(78), virtualHeight = GetSystemMetrics(79) };
    internal delegate bool EnumProc(nint window, nint parameter);
    internal delegate void WinEventProc(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint parameter);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    internal static PointerSample ReadPrimaryPointer()
    {
        if (!GetCursorPos(out var point)) return new(false, 0, 0, false);
        var primary = GetSystemMetrics(23) != 0 ? 2 : 1;
        return new((GetAsyncKeyState(primary) & 0x8000) != 0, point.X, point.Y);
    }
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    internal static long RootWindowAt(int x, int y) => (long)GetAncestor(WindowFromPoint(new Point { X = x, Y = y }), 2);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder value, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder value, ref uint count);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFullName(nint process, ref uint count, StringBuilder? package);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint value);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out Rect value, uint size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int GetCloaked(nint window, uint attribute, out int value, uint size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint first, uint last, nint module, WinEventProc callback, uint processId, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);

    internal static bool IsCodexSurface(WindowIdentity value) => WindowIdentityPolicy.IsCodexSurface(value.Package, value.ClassName, value.Bounds.Width, value.Bounds.Height);
    internal static IReadOnlyList<WindowIdentity> DiscoverCodex()
    {
        var windows = new List<WindowIdentity>();
        EnumWindows((window, _) =>
        {
            if (IsWindowVisible(window) && Read(window) is { } identity && IsCodexSurface(identity))
                windows.Add(identity);
            return true;
        }, 0);
        return windows;
    }

    internal static WindowIdentity? Read(nint window)
    {
        if (!IsWindow(window)) return null;
        GetWindowThreadProcessId(window, out var pid);
        var process = OpenProcess(0x1000, false, pid);
        if (process == 0) return null;
        try
        {
            var path = new StringBuilder(32768);
            uint count = 32768;
            if (!QueryFullProcessImageName(process, 0, path, ref count)) return null;
            uint packageLength = 0;
            GetPackageFullName(process, ref packageLength, null);
            var package = new StringBuilder((int)Math.Clamp(packageLength, 1, 32768));
            if (packageLength > 0) GetPackageFullName(process, ref packageLength, package);
            var className = new StringBuilder(256);
            GetClassName(window, className, 256);
            if (DwmGetWindowAttribute(window, 9, out var bounds, 16) != 0 && !GetWindowRect(window, out bounds))
                return null;
            GetCloaked(window, 14, out var cloaked, 4);
            return new((long)window, pid, path.ToString(), package.ToString(), className.ToString(),
                new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), GetDpiForWindow(window),
                IsWindowVisible(window), IsIconic(window), cloaked != 0);
        }
        finally { CloseHandle(process); }
    }

    internal static void Position(nint window, OverlayPlacement placement)
    {
        if (!SetWindowPos(window, -1, placement.Left, placement.Top, placement.Width, placement.Height, 0x0010 | 0x0200 | 0x0040))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}








