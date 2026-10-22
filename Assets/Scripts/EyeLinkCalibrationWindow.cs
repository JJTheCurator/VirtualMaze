#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

/// <summary>
/// Temporary native window used by EyeLink's GDI calibration interface.
/// Keeping calibration graphics off Unity's Direct3D surface prevents Unity
/// from repainting over the camera image and calibration targets.
/// </summary>
internal sealed class EyeLinkCalibrationWindow : Form
{
    [DllImport("user32.dll")]
    private static extern void DisableProcessWindowsGhosting();

    private bool cursorHidden;
    private readonly Queue<int> setupKeys = new Queue<int>();

    internal string TargetMonitorDeviceName { get; private set; }
    internal System.Drawing.Rectangle TargetMonitorBounds { get; private set; }

    internal EyeLinkCalibrationWindow(IntPtr unityWindow)
    {
        FormBorderStyle = FormBorderStyle.None;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;

        Screen desktop = Screen.FromHandle(unityWindow);
        Screen[] windowsScreens = Screen.AllScreens;
        if (windowsScreens.Length > 1) {
            TargetMonitorDeviceName = windowsScreens[1].DeviceName;
            TargetMonitorBounds = windowsScreens[1].Bounds;
        }
        else if (UnityEngine.Display.displays.Length > 1) {
            // This machine exposes one long Windows desktop even though Unity
            // detects two displays. Display 2 is the right-hand section.
            UnityEngine.Display display2 = UnityEngine.Display.displays[1];
            int width = display2.systemWidth;
            int height = display2.systemHeight;
            if (width <= 0) {
                width = desktop.Bounds.Width / 2;
            }
            if (height <= 0) {
                height = desktop.Bounds.Height;
            }

            width = Math.Min(width, desktop.Bounds.Width);
            height = Math.Min(height, desktop.Bounds.Height);
            int left = desktop.Bounds.Left +
                UnityEngine.Display.displays[0].systemWidth;
            if (UnityEngine.Display.displays[0].systemWidth <= 0) {
                left = desktop.Bounds.Right - width;
            }

            // Keep the rectangle within the combined desktop if Windows or
            // DPI scaling reports slightly different logical dimensions.
            left = Math.Min(left, desktop.Bounds.Right - width);
            left = Math.Max(left, desktop.Bounds.Left);

            TargetMonitorDeviceName = "Display 2";
            TargetMonitorBounds = new System.Drawing.Rectangle(
                left,
                desktop.Bounds.Top,
                width,
                height);
        }
        else {
            TargetMonitorDeviceName = desktop.DeviceName;
            TargetMonitorBounds = desktop.Bounds;
        }

        Bounds = TargetMonitorBounds;

        // Tracker setup blocks Unity's normal update loop while it is open.
        DisableProcessWindowsGhosting();
    }

    internal void ShowForCalibration()
    {
        Show();
        Bounds = TargetMonitorBounds;
        Application.DoEvents();

        Activate();
        Focus();

        Cursor.Hide();
        cursorHidden = true;
        Application.DoEvents();
    }

    internal bool TryDequeueSetupKey(out int key)
    {
        if (setupKeys.Count == 0) {
            key = 0;
            return false;
        }

        key = setupKeys.Dequeue();
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int key = 0;
        if (e.KeyCode == Keys.Escape) {
            key = 0x001B;
        }
        else if (e.KeyCode == Keys.Enter) {
            key = 0x000D;
        }
        else if (e.KeyCode == Keys.Space) {
            key = ' ';
        }
        else if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z) {
            key = Char.ToLowerInvariant((char)e.KeyValue);
        }
        else if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) {
            key = '0' + ((int)e.KeyCode - (int)Keys.D0);
        }

        if (key != 0) {
            setupKeys.Enqueue(key);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (cursorHidden) {
            Cursor.Show();
            cursorHidden = false;
        }

        base.Dispose(disposing);
    }
}
#endif
