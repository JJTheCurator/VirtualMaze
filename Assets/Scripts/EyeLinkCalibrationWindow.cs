#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
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
        Bounds = System.Windows.Forms.Screen.FromHandle(unityWindow).Bounds;

        // Tracker setup blocks Unity's normal update loop while it is open.
        DisableProcessWindowsGhosting();
    }

    internal void ShowForCalibration()
    {
        Show();
        Activate();
        Focus();

        Cursor.Hide();
        cursorHidden = true;
        Application.DoEvents();
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
