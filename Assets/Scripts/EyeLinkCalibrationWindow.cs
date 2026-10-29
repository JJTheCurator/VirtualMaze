#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

/// <summary>
/// Native GDI surface for EyeLink. Avoids Mono WinForms' window-class cache
/// across Unity Play Mode reloads. Use only from Unity's main thread.
/// </summary>
internal sealed class EyeLinkCalibrationWindow : IDisposable
{
    private readonly Queue<int> setupKeys = new Queue<int>();
    private readonly WindowProcedure windowProcedure;
    private readonly string windowClassName;
    private readonly IntPtr instance;
    private bool classRegistered;

    internal IntPtr Handle { get; private set; }
    internal string TargetMonitorDeviceName { get; private set; }
    internal Rectangle TargetMonitorBounds { get; private set; }
    internal Size ClientSize {
        get {
            NativeRect client;
            if (!GetClientRect(Handle, out client)) {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Could not read the EyeLink calibration window size.");
            }
            return client.ToRectangle().Size;
        }
    }

    internal EyeLinkCalibrationWindow(IntPtr unityWindow)
    {
        SelectMonitor(unityWindow);
        // A distinct name prevents collisions after Unity domain reloads.
        windowClassName = "VirtualMaze.EyeLink." + Guid.NewGuid().ToString("N");
        windowProcedure = ProcessWindowMessage;
        instance = GetModuleHandle(null);
        WindowClass definition = new WindowClass {
            size = (uint)Marshal.SizeOf(typeof(WindowClass)),
            style = 0x0020, // CS_OWNDC: stable GDI context for the SDK.
            procedure = windowProcedure,
            instance = instance,
            background = GetStockObject(4), // BLACK_BRUSH; owned by Windows.
            className = windowClassName
        };
        if (RegisterClassEx(ref definition) == 0) {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not register the native EyeLink calibration window.");
        }
        classRegistered = true;
        Handle = CreateWindowEx(0x00000080, windowClassName, "EyeLink Calibration",
            0x80000000, TargetMonitorBounds.X, TargetMonitorBounds.Y,
            TargetMonitorBounds.Width, TargetMonitorBounds.Height,
            unityWindow, IntPtr.Zero, instance, IntPtr.Zero);
        if (Handle == IntPtr.Zero) {
            int error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error,
                "Could not create the native EyeLink calibration window.");
        }
    }

    private void SelectMonitor(IntPtr unityWindow)
    {
        List<MonitorInfo> monitors = new List<MonitorInfo>();
        MonitorProcedure enumerate = delegate(IntPtr monitor, IntPtr dc,
            ref NativeRect rectangle, IntPtr data) {
            MonitorInfo info = new MonitorInfo();
            info.size = (uint)Marshal.SizeOf(typeof(MonitorInfo));
            if (GetMonitorInfo(monitor, ref info)) {
                monitors.Add(info);
            }
            return true;
        };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, enumerate, IntPtr.Zero) ||
            monitors.Count == 0) {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not enumerate Windows displays for EyeLink.");
        }
        MonitorInfo selected = monitors[0];
        if (monitors.Count > 1) {
            selected = monitors[1];
            foreach (MonitorInfo monitor in monitors) {
                if (monitor.deviceName.EndsWith("DISPLAY2",
                    StringComparison.OrdinalIgnoreCase)) {
                    selected = monitor;
                    break;
                }
            }
        }
        else {
            MonitorInfo ownerMonitor = new MonitorInfo();
            ownerMonitor.size = (uint)Marshal.SizeOf(typeof(MonitorInfo));
            if (GetMonitorInfo(MonitorFromWindow(unityWindow, 2), ref ownerMonitor)) {
                selected = ownerMonitor;
            }
        }
        TargetMonitorDeviceName = selected.deviceName;
        TargetMonitorBounds = selected.monitor.ToRectangle();

        if (monitors.Count == 1 && UnityEngine.Display.displays.Length > 1) {
            // Preserve the combined-desktop fallback used by this installation.
            Rectangle desktop = TargetMonitorBounds;
            UnityEngine.Display display2 = UnityEngine.Display.displays[1];
            int width = display2.systemWidth > 0
                ? Math.Min(display2.systemWidth, desktop.Width) : desktop.Width / 2;
            int height = display2.systemHeight > 0
                ? Math.Min(display2.systemHeight, desktop.Height) : desktop.Height;
            int left = desktop.Left + UnityEngine.Display.displays[0].systemWidth;
            if (UnityEngine.Display.displays[0].systemWidth <= 0) {
                left = desktop.Right - width;
            }
            left = Math.Max(desktop.Left, Math.Min(left, desktop.Right - width));
            TargetMonitorBounds = new Rectangle(left, desktop.Top, width, height);
            TargetMonitorDeviceName = "Display 2 (combined desktop)";
        }
    }

    internal void ShowForCalibration()
    {
        DisableProcessWindowsGhosting();
        // Let the debugger be raised over the calibration display in the Editor.
#if UNITY_EDITOR
        IntPtr order = IntPtr.Zero; // HWND_TOP
#else
        IntPtr order = new IntPtr(-1); // HWND_TOPMOST
#endif
        if (!SetWindowPos(Handle, order, TargetMonitorBounds.X,
            TargetMonitorBounds.Y, TargetMonitorBounds.Width,
            TargetMonitorBounds.Height, 0x0040)) { // SWP_SHOWWINDOW
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not show the EyeLink calibration window.");
        }
        SetForegroundWindow(Handle);
        SetFocus(Handle);
        SetCursor(IntPtr.Zero);
        PumpEvents();
    }

    internal void PumpEvents()
    {
        NativeMessage message;
        // Include thread COM messages, as well as calibration input.
        for (int count = 0; count < 256 &&
            PeekMessage(out message, IntPtr.Zero, 0, 0, 1); count++) {
            if (message.message == 0x0012) { // WM_QUIT
                setupKeys.Enqueue(0x001B);
                PostQuitMessage(message.wParam.ToInt32());
                break;
            }
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    internal bool TryDequeueSetupKey(out int key)
    {
        key = setupKeys.Count == 0 ? 0 : setupKeys.Dequeue();
        return key != 0;
    }

    private IntPtr ProcessWindowMessage(IntPtr window, uint message,
        IntPtr wParam, IntPtr lParam)
    {
        if (message == 0x0010) { // WM_CLOSE: allow the tracker to exit first.
            setupKeys.Enqueue(0x001B);
            return IntPtr.Zero;
        }
        if (message == 0x0020 && (lParam.ToInt64() & 0xFFFF) == 1) {
            SetCursor(IntPtr.Zero); // WM_SETCURSOR, HTCLIENT
            return new IntPtr(1);
        }
        if (message == 0x0100) { // WM_KEYDOWN
            int key = wParam.ToInt32();
            if (key >= 'A' && key <= 'Z') {
                key += 'a' - 'A';
            }
            if (key == 0x001B || key == 0x000D || key == ' ' ||
                (key >= 'a' && key <= 'z') || (key >= '0' && key <= '9')) {
                setupKeys.Enqueue(key);
                return IntPtr.Zero;
            }
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) {
            DestroyWindow(Handle);
            Handle = IntPtr.Zero;
            SetCursor(LoadCursor(IntPtr.Zero, new IntPtr(32512))); // IDC_ARROW
        }
        if (classRegistered) {
            UnregisterClass(windowClassName, instance);
            classRegistered = false;
        }
        GC.KeepAlive(windowProcedure);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(IntPtr window, uint message,
        IntPtr wParam, IntPtr lParam);
    private delegate bool MonitorProcedure(IntPtr monitor, IntPtr dc,
        ref NativeRect rectangle, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass {
        public uint size, style;
        public WindowProcedure procedure;
        public int classExtra, windowExtra;
        public IntPtr instance, icon, cursor, background;
        public string menuName, className;
        public IntPtr smallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect {
        public int left, top, right, bottom;
        public Rectangle ToRectangle() {
            return Rectangle.FromLTRB(left, top, right, bottom);
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo {
        public uint size;
        public NativeRect monitor, work;
        public uint flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string deviceName;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage {
        public IntPtr window;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int x, y;
        public uint privateData;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string name);
    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass definition);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string name, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className,
        string title, uint style, int x, int y, int width, int height,
        IntPtr owner, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip,
        MonitorProcedure procedure, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr order,
        int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message,
        IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessage(out NativeMessage message,
        IntPtr window, uint minimum, uint maximum, uint remove);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);
    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll")]
    private static extern void DisableProcessWindowsGhosting();
}
#endif
