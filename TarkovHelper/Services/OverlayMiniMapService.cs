using System.Text.Json;
using TarkovHelper.Models.Map;
using TarkovHelper.Services.Logging;
using TarkovHelper.Windows;
using TarkovHelper.Windows.Dialogs;

namespace TarkovHelper.Services;

/// <summary>
/// 오버레이 미니맵 서비스 - 오버레이 윈도우 생명주기 및 설정 관리
/// </summary>
public class OverlayMiniMapService : IDisposable
{
    private static readonly ILogger _log = Log.For<OverlayMiniMapService>();
    private static OverlayMiniMapService? _instance;
    private static readonly object _lock = new();

    public static OverlayMiniMapService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new OverlayMiniMapService();
                }
            }
            return _instance;
        }
    }

    private OverlayMiniMapWindow? _overlayWindow;
    private OverlayMiniMapSettings _settings;
    private bool _isInitialized;

    /// <summary>
    /// 오버레이 표시 상태 변경 이벤트
    /// </summary>
    public event Action<bool>? OverlayVisibilityChanged;

    /// <summary>
    /// 설정 변경 이벤트
    /// </summary>
    public event Action<OverlayMiniMapSettings>? SettingsChanged;

    /// <summary>
    /// 현재 설정
    /// </summary>
    public OverlayMiniMapSettings Settings => _settings;

    /// <summary>
    /// 오버레이 표시 여부
    /// </summary>
    public bool IsOverlayVisible => _overlayWindow?.IsVisible == true;

    private OverlayMiniMapService()
    {
        _settings = new OverlayMiniMapSettings();
    }

    /// <summary>
    /// 서비스 초기화
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            await LoadSettingsAsync();

            // 단축키 이벤트 연결
            GlobalKeyboardHookService.Instance.OverlayTogglePressed += OnOverlayTogglePressed;
            GlobalKeyboardHookService.Instance.OverlaySettingsPressed += OnSettingsPressed;
            GlobalKeyboardHookService.Instance.OverlayZoomInPressed += OnZoomInPressed;
            GlobalKeyboardHookService.Instance.OverlayZoomOutPressed += OnZoomOutPressed;

            _isInitialized = true;
            _log.Info("OverlayMiniMapService initialized");

            // 이전 상태가 활성화였다면 오버레이 표시
            if (_settings.Enabled)
            {
                ShowOverlay();
            }
        }
        catch (Exception ex)
        {
            _log.Error("Failed to initialize OverlayMiniMapService", ex);
        }
    }

    #region Overlay Window Management

    /// <summary>
    /// 오버레이 표시
    /// </summary>
    public void ShowOverlay()
    {
        try
        {
            if (_overlayWindow == null)
            {
                CreateOverlayWindow();
            }

            _overlayWindow?.Show();
            _settings.Enabled = true;
            OverlayVisibilityChanged?.Invoke(true);

            _log.Debug("Overlay shown");
        }
        catch (Exception ex)
        {
            _log.Error("Failed to show overlay", ex);
        }
    }

    /// <summary>
    /// 오버레이 숨기기
    /// </summary>
    public void HideOverlay()
    {
        try
        {
            _overlayWindow?.Hide();
            _settings.Enabled = false;
            OverlayVisibilityChanged?.Invoke(false);

            _log.Debug("Overlay hidden");
        }
        catch (Exception ex)
        {
            _log.Error("Failed to hide overlay", ex);
        }
    }

    /// <summary>
    /// 오버레이 토글
    /// </summary>
    public void ToggleOverlay()
    {
        if (IsOverlayVisible)
            HideOverlay();
        else
            ShowOverlay();
    }

    private void CreateOverlayWindow()
    {
        _overlayWindow = new OverlayMiniMapWindow(_settings);
        _overlayWindow.SettingsChanged += OnOverlaySettingsChanged;
        _overlayWindow.OverlayClosed += OnOverlayClosed;
    }

    private void OnOverlaySettingsChanged(OverlayMiniMapSettings settings)
    {
        _settings = settings;
        _ = SaveSettingsAsync();
        SettingsChanged?.Invoke(settings);
    }

    private void OnOverlayClosed()
    {
        _settings.Enabled = false;
        OverlayVisibilityChanged?.Invoke(false);
        _ = SaveSettingsAsync();
    }

    #endregion

    #region Keyboard Shortcuts

    private void OnOverlayTogglePressed()
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            ToggleOverlay();
        });
    }

    private void OnSettingsPressed()
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            ShowSettingsWindow();
        });
    }

    private void OnZoomInPressed()
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_overlayWindow != null && IsOverlayVisible)
            {
                _overlayWindow.ZoomIn();
                _log.Debug($"Overlay zoom in: {_settings.ZoomLevel:F2}");
            }
        });
    }

    private void OnZoomOutPressed()
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_overlayWindow != null && IsOverlayVisible)
            {
                _overlayWindow.ZoomOut();
                _log.Debug($"Overlay zoom out: {_settings.ZoomLevel:F2}");
            }
        });
    }

    #endregion

    #region Settings Persistence

    private const string SettingsKey = "overlayMiniMap.settings";
    private readonly UserDataDbService _userDataDb = UserDataDbService.Instance;

    private async Task LoadSettingsAsync()
    {
        try
        {
            var json = await _userDataDb.GetSettingAsync(SettingsKey);
            if (!string.IsNullOrEmpty(json))
            {
                var loaded = JsonSerializer.Deserialize<OverlayMiniMapSettings>(json);
                if (loaded != null)
                {
                    _settings = loaded;
                    _log.Debug("Overlay settings loaded from database");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to load overlay settings: {ex.Message}");
            _settings = new OverlayMiniMapSettings();
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings);
            await _userDataDb.SetSettingAsync(SettingsKey, json);
            _log.Debug("Overlay settings saved to database");
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to save overlay settings: {ex.Message}");
        }
    }

    /// <summary>
    /// 설정 저장 (동기)
    /// </summary>
    public void SaveSettings()
    {
        _ = SaveSettingsAsync();
    }

    /// <summary>
    /// 설정 초기화
    /// </summary>
    public void ResetSettings()
    {
        _settings.ResetToDefaults();
        _ = SaveSettingsAsync();

        if (_overlayWindow != null)
        {
            _overlayWindow.Close();
            _overlayWindow = null;

            if (_settings.Enabled)
            {
                ShowOverlay();
            }
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 맵 새로고침
    /// </summary>
    public void RefreshMap()
    {
        _overlayWindow?.RefreshMap();
    }

    private OverlaySettingsWindow? _settingsWindow;

    /// <summary>
    /// 설정창 열기
    /// </summary>
    public void ShowSettingsWindow()
    {
        if (_settingsWindow == null || !_settingsWindow.IsVisible)
        {
            _settingsWindow = new OverlaySettingsWindow(_settings, _overlayWindow);
            _settingsWindow.SettingsApplied += OnSettingsApplied;
            _settingsWindow.Show();
        }
        else
        {
            _settingsWindow.Activate();
        }
    }

    private void OnSettingsApplied(OverlayMiniMapSettings settings)
    {
        _settings.CopyFrom(settings);
        _ = SaveSettingsAsync();
        SettingsChanged?.Invoke(_settings);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        GlobalKeyboardHookService.Instance.OverlayTogglePressed -= OnOverlayTogglePressed;
        GlobalKeyboardHookService.Instance.OverlaySettingsPressed -= OnSettingsPressed;
        GlobalKeyboardHookService.Instance.OverlayZoomInPressed -= OnZoomInPressed;
        GlobalKeyboardHookService.Instance.OverlayZoomOutPressed -= OnZoomOutPressed;

        _overlayWindow?.Close();
        _overlayWindow = null;

        _log.Info("OverlayMiniMapService disposed");

        GC.SuppressFinalize(this);
    }

    #endregion
}
