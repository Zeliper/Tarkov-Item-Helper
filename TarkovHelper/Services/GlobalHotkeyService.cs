using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace TarkovHelper.Services;

/// <summary>
/// Global keyboard hook service for capturing hotkeys when EscapeFromTarkov.exe is in foreground
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    #region Win32 API

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    #endregion

    private static GlobalHotkeyService? _instance;
    public static GlobalHotkeyService Instance => _instance ??= new GlobalHotkeyService();

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private bool _isHooked;
    private bool _disposed;

    /// <summary>
    /// Event fired when NumPad key (0-5) is pressed while EFT or TarkovHelper is in foreground
    /// </summary>
    public event EventHandler<FloorHotkeyEventArgs>? FloorHotkeyPressed;

    private GlobalHotkeyService()
    {
    }

    /// <summary>
    /// Start the global keyboard hook
    /// </summary>
    public void StartHook()
    {
        if (_isHooked) return;

        _proc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;

        if (curModule != null)
        {
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
            _isHooked = _hookId != IntPtr.Zero;
        }
    }

    /// <summary>
    /// Stop the global keyboard hook
    /// </summary>
    public void StopHook()
    {
        if (!_isHooked) return;

        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        _isHooked = false;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            var key = KeyInterop.KeyFromVirtualKey(vkCode);

            // Check if it's a NumPad key 0-5
            int? floorIndex = key switch
            {
                Key.NumPad0 => 0,
                Key.NumPad1 => 1,
                Key.NumPad2 => 2,
                Key.NumPad3 => 3,
                Key.NumPad4 => 4,
                Key.NumPad5 => 5,
                _ => null
            };

            if (floorIndex.HasValue && IsTargetProcessForeground())
            {
                FloorHotkeyPressed?.Invoke(this, new FloorHotkeyEventArgs(floorIndex.Value));
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Check if EscapeFromTarkov.exe is the foreground window
    /// </summary>
    private bool IsTargetProcessForeground()
    {
        try
        {
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero) return false;

            GetWindowThreadProcessId(foregroundWindow, out uint processId);

            using var process = Process.GetProcessById((int)processId);
            var processName = process.ProcessName;

            // Check for EscapeFromTarkov.exe
            return processName.Equals("EscapeFromTarkov", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopHook();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~GlobalHotkeyService()
    {
        Dispose();
    }
}

/// <summary>
/// Event args for floor hotkey press
/// </summary>
public class FloorHotkeyEventArgs : EventArgs
{
    /// <summary>
    /// Floor index (0-5)
    /// </summary>
    public int FloorIndex { get; }

    public FloorHotkeyEventArgs(int floorIndex)
    {
        FloorIndex = floorIndex;
    }
}
