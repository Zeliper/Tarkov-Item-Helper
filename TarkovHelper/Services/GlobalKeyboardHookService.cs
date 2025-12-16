using System;
using System.IO;
using System.Runtime.InteropServices;
using SysDiag = System.Diagnostics;

namespace TarkovHelper.Services;

/// <summary>
/// Global keyboard hook service that captures keyboard events even when the app is not in focus.
/// Used to capture NumPad keys for floor selection when EscapeFromTarkov.exe is the foreground window.
/// </summary>
public class GlobalKeyboardHookService : IDisposable
{
    #region Win32 API

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    #endregion

    public static GlobalKeyboardHookService Instance { get; } = new();

    private IntPtr _hookId = IntPtr.Zero;
    // Keep delegate reference to prevent GC collection
    private readonly LowLevelKeyboardProc _proc;
    private bool _isHooked;
    private bool _isEnabled;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TarkovHelper", "keyboard_hook.log");

    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    /// <summary>
    /// Event fired when a NumPad key is pressed while EscapeFromTarkov.exe is the foreground window.
    /// Parameter is the floor index (0-5).
    /// </summary>
    public event Action<int>? FloorKeyPressed;

    /// <summary>
    /// Whether the global hook is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;

            if (_isEnabled)
                StartHook();
            else
                StopHook();
        }
    }

    private GlobalKeyboardHookService()
    {
        // Initialize delegate in constructor to prevent GC collection
        _proc = HookCallback;
    }

    private void StartHook()
    {
        if (_isHooked) return;

        // Use null for hMod with WH_KEYBOARD_LL - this is the correct approach for low-level hooks
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
        _isHooked = _hookId != IntPtr.Zero;

        Log($"StartHook: hookId={_hookId}, isHooked={_isHooked}");
        if (!_isHooked)
        {
            var error = Marshal.GetLastWin32Error();
            Log($"SetWindowsHookEx failed with error: {error}");
        }
    }

    private void StopHook()
    {
        if (!_isHooked || _hookId == IntPtr.Zero) return;

        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        _isHooked = false;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            int? floorIndex = GetFloorIndexFromVkCode(vkCode);

            // Only process NumPad keys
            if (floorIndex.HasValue)
            {
                Log($"NumPad{floorIndex.Value} key detected (vkCode={vkCode})");

                // Check if EscapeFromTarkov.exe or TarkovHelper is the foreground window
                if (IsTarkovOrHelperForeground())
                {
                    Log($"NumPad{floorIndex.Value} pressed, firing event");
                    // Fire event on UI thread
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        FloorKeyPressed?.Invoke(floorIndex.Value);
                    });
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Checks if EscapeFromTarkov.exe or TarkovHelper is the foreground window.
    /// </summary>
    private static bool IsTarkovOrHelperForeground()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == 0) return false;

            using var process = SysDiag.Process.GetProcessById((int)processId);
            var processName = process.ProcessName;

            Log($"Foreground process: '{processName}'");

            // Allow both Tarkov and TarkovHelper for testing
            // Check for various possible Tarkov process names
            return processName.Contains("Tarkov", StringComparison.OrdinalIgnoreCase)
                || processName.Contains("EFT", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log($"IsTarkovOrHelperForeground error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the floor index from a virtual key code.
    /// NumPad0 = 0, NumPad1 = 1, ..., NumPad5 = 5
    /// </summary>
    private static int? GetFloorIndexFromVkCode(int vkCode)
    {
        // Virtual key codes for NumPad keys
        const int VK_NUMPAD0 = 0x60;
        const int VK_NUMPAD1 = 0x61;
        const int VK_NUMPAD2 = 0x62;
        const int VK_NUMPAD3 = 0x63;
        const int VK_NUMPAD4 = 0x64;
        const int VK_NUMPAD5 = 0x65;

        return vkCode switch
        {
            VK_NUMPAD0 => 0,
            VK_NUMPAD1 => 1,
            VK_NUMPAD2 => 2,
            VK_NUMPAD3 => 3,
            VK_NUMPAD4 => 4,
            VK_NUMPAD5 => 5,
            _ => null
        };
    }

    public void Dispose()
    {
        StopHook();
        GC.SuppressFinalize(this);
    }

    ~GlobalKeyboardHookService()
    {
        StopHook();
    }
}
