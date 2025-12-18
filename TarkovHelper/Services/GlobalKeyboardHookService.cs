using System;
using System.IO;
using System.Media;
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

    // 붕권 커맨드: ↓↘→ + O (S → S+D → D → O)
    // 방향 입력: Down(1), DownForward(2), Forward(3), 그리고 O(4)
    private readonly int[] _secretCommandSequence = { 1, 2, 3, 4 }; // Down, DownForward, Forward, O
    private readonly List<int> _commandBuffer = new();
    private DateTime _lastCommandKeyTime = DateTime.MinValue;
    private const int CommandTimeoutMs = 800; // 0.8초 내에 입력해야 함

    // Virtual key codes
    private const int VK_S = 0x53;  // S = Down
    private const int VK_D = 0x44;  // D = Forward
    private const int VK_O = 0x4F;  // O = 오른손

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

    #region Overlay MiniMap Hotkey Events

    /// <summary>
    /// 오버레이 토글 (Ctrl+M)
    /// </summary>
    public event Action? OverlayTogglePressed;

    /// <summary>
    /// 오버레이 설정창 열기 (Ctrl+L)
    /// </summary>
    public event Action? OverlaySettingsPressed;

    /// <summary>
    /// 오버레이 줌 인 (NumPad +)
    /// </summary>
    public event Action? OverlayZoomInPressed;

    /// <summary>
    /// 오버레이 줌 아웃 (NumPad -)
    /// </summary>
    public event Action? OverlayZoomOutPressed;

    #endregion

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

            // Check if EscapeFromTarkov.exe or TarkovHelper is the foreground window
            if (IsTarkovOrHelperForeground())
            {
                // 붕권 커맨드 체크 (S S D D O)
                CheckSecretCommand(vkCode);

                // NumPad keys for floor selection (0-5)
                int? floorIndex = GetFloorIndexFromVkCode(vkCode);
                if (floorIndex.HasValue)
                {
                    Log($"NumPad{floorIndex.Value} pressed, firing event");
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        FloorKeyPressed?.Invoke(floorIndex.Value);
                    });
                }

                // NumPad +/- for overlay zoom (오버레이 활성화 시에만)
                if (IsOverlayVisible)
                {
                    var zoomAction = GetZoomActionFromVkCode(vkCode);
                    if (zoomAction != null)
                    {
                        Log($"NumPad zoom key pressed (overlay visible)");
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            zoomAction.Invoke();
                        });
                    }
                }

                // Overlay hotkeys (Ctrl + key) - 오버레이 활성화 시에만
                if (IsCtrlPressed() && IsOverlayVisible)
                {
                    var overlayAction = GetOverlayActionFromVkCode(vkCode);
                    if (overlayAction != null)
                    {
                        Log($"Overlay hotkey detected");
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            overlayAction.Invoke();
                        });
                    }
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Ctrl 키가 눌려있는지 확인
    /// </summary>
    private static bool IsCtrlPressed()
    {
        return (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0 ||
               (GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;

    /// <summary>
    /// 오버레이가 현재 표시 중인지 확인
    /// </summary>
    private static bool IsOverlayVisible => OverlayMiniMapService.Instance.IsOverlayVisible;

    /// <summary>
    /// 붕권 커맨드 체크 (↓↘→ + O)
    /// </summary>
    private void CheckSecretCommand(int vkCode)
    {
        var now = DateTime.Now;

        // 타임아웃 체크 - 시간 초과시 버퍼 리셋
        if ((now - _lastCommandKeyTime).TotalMilliseconds > CommandTimeoutMs)
        {
            _commandBuffer.Clear();
        }

        // 방향 입력 변환
        int? direction = GetDirectionInput(vkCode);
        if (direction == null) return;

        _lastCommandKeyTime = now;

        int expectedIndex = _commandBuffer.Count;

        // 현재 입력이 시퀀스의 다음 예상 입력과 일치하는지 확인
        if (expectedIndex < _secretCommandSequence.Length && direction == _secretCommandSequence[expectedIndex])
        {
            _commandBuffer.Add(direction.Value);
            string dirName = direction switch { 1 => "↓", 2 => "↘", 3 => "→", 4 => "O", _ => "?" };
            Log($"Command input: {dirName} ({_commandBuffer.Count}/{_secretCommandSequence.Length})");

            // 시퀀스 완료!
            if (_commandBuffer.Count == _secretCommandSequence.Length)
            {
                Log("*** COMMAND SUCCESS! ↓↘→+O ***");
                _commandBuffer.Clear();

                // 효과음 재생 & 오버레이 토글
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    PlayCommandSound();
                    OverlayTogglePressed?.Invoke();
                });
            }
        }
        else if (direction == _secretCommandSequence[0])
        {
            // 첫 입력(↓)으로 다시 시작
            _commandBuffer.Clear();
            _commandBuffer.Add(direction.Value);
            Log($"Command input: ↓ (1/{_secretCommandSequence.Length})");
        }
        else
        {
            // 잘못된 입력 - 버퍼 리셋
            _commandBuffer.Clear();
        }
    }

    /// <summary>
    /// 키 입력을 방향 입력으로 변환
    /// </summary>
    private int? GetDirectionInput(int vkCode)
    {
        bool sPressed = (GetAsyncKeyState(VK_S) & 0x8000) != 0;
        bool dPressed = (GetAsyncKeyState(VK_D) & 0x8000) != 0;

        // S 키 다운
        if (vkCode == VK_S)
        {
            if (dPressed)
                return 2; // ↘ (S+D 동시)
            else
                return 1; // ↓ (S만)
        }
        // D 키 다운
        else if (vkCode == VK_D)
        {
            if (sPressed)
                return 2; // ↘ (S+D 동시)
            else
                return 3; // → (D만)
        }
        // O 키 다운
        else if (vkCode == VK_O)
        {
            return 4; // O (공격)
        }

        return null;
    }

    /// <summary>
    /// 커맨드 성공 효과음 재생
    /// </summary>
    private static void PlayCommandSound()
    {
        try
        {
            // 커맨드 성공음 파일 경로
            var soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Sounds", "command_success.wav");

            if (File.Exists(soundPath))
            {
                using var player = new SoundPlayer(soundPath);
                player.Play();
            }
            else
            {
                // 파일이 없으면 시스템 사운드
                SystemSounds.Exclamation.Play();
            }
        }
        catch
        {
            // 사운드 재생 실패해도 무시
        }
    }

    /// <summary>
    /// Virtual key code를 오버레이 액션으로 변환 (Ctrl + key, 오버레이 활성화 시에만)
    /// </summary>
    private Action? GetOverlayActionFromVkCode(int vkCode)
    {
        const int VK_L = 0x4C;  // L - 설정창 열기

        return vkCode switch
        {
            VK_L => OverlaySettingsPressed,
            _ => null
        };
    }

    /// <summary>
    /// Virtual key code를 줌 액션으로 변환 (NumPad +/-)
    /// </summary>
    private Action? GetZoomActionFromVkCode(int vkCode)
    {
        const int VK_ADD = 0x6B;         // NumPad +
        const int VK_SUBTRACT = 0x6D;    // NumPad -

        return vkCode switch
        {
            VK_ADD => OverlayZoomInPressed,
            VK_SUBTRACT => OverlayZoomOutPressed,
            _ => null
        };
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

            // Allow Tarkov game and TarkovHelper app (including when running via dotnet)
            return processName.Contains("Tarkov", StringComparison.OrdinalIgnoreCase)
                || processName.Contains("EFT", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("TarkovHelper", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase);
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
