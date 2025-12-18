using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TarkovHelper.Models.Map;
using TarkovHelper.Services.Map;

namespace TarkovHelper.Pages.Map.Components;

/// <summary>
/// 맵 좌표 보정(Calibration) 모드를 담당하는 클래스.
/// 탈출구 마커를 드래그하여 실제 게임 좌표와 화면 좌표를 매핑하는 로직을 캡슐화합니다.
/// </summary>
public class MapCalibrationController
{
    // 의존성 서비스
    private readonly Canvas _extractMarkersContainer;
    private readonly MapTrackerService _trackerService;
    private readonly MapCalibrationService _calibrationService;

    // 보정 모드 상태
    private bool _isCalibrationMode;
    private string? _currentMapKey;

    // 드래그 상태
    private FrameworkElement? _draggingExtractMarker;
    private Point _extractDragStartPoint;
    private double _extractMarkerOriginalLeft;
    private double _extractMarkerOriginalTop;
    private MapExtract? _draggingExtract;

    // 이벤트
    /// <summary>
    /// 상태 메시지 업데이트 이벤트 (TxtStatus.Text에 표시할 메시지)
    /// </summary>
    public event Action<string>? StatusUpdated;

    /// <summary>
    /// 캘리브레이션 완료 이벤트 (마커 새로고침 필요)
    /// </summary>
    public event EventHandler? CalibrationCompleted;

    // 프로퍼티
    /// <summary>
    /// 현재 캘리브레이션 모드 활성화 여부
    /// </summary>
    public bool IsCalibrationMode => _isCalibrationMode;

    public MapCalibrationController(
        Canvas extractMarkersContainer,
        MapTrackerService trackerService,
        MapCalibrationService calibrationService)
    {
        _extractMarkersContainer = extractMarkersContainer ?? throw new ArgumentNullException(nameof(extractMarkersContainer));
        _trackerService = trackerService ?? throw new ArgumentNullException(nameof(trackerService));
        _calibrationService = calibrationService ?? throw new ArgumentNullException(nameof(calibrationService));
    }

    #region Public Methods - Mode Control

    /// <summary>
    /// 캘리브레이션 모드 활성화
    /// </summary>
    public void EnterCalibrationMode()
    {
        _isCalibrationMode = true;
        StatusUpdated?.Invoke("Calibration mode enabled. Drag extract markers to set calibration points.");
    }

    /// <summary>
    /// 캘리브레이션 모드 비활성화
    /// </summary>
    public void ExitCalibrationMode()
    {
        _isCalibrationMode = false;
        _draggingExtractMarker = null;
        _draggingExtract = null;
        StatusUpdated?.Invoke("Calibration mode disabled.");
    }

    /// <summary>
    /// 현재 맵 설정
    /// </summary>
    public void SetCurrentMap(string? mapKey)
    {
        _currentMapKey = mapKey;
    }

    #endregion

    #region Public Methods - Calibration

    /// <summary>
    /// 캘리브레이션 저장 및 마커 새로고침 요청
    /// </summary>
    public void SaveCalibrationAndRefresh()
    {
        if (_trackerService == null) return;

        var config = _trackerService.GetMapConfig(_currentMapKey ?? "");
        if (config?.CalibrationPoints != null && config.CalibrationPoints.Count >= 3)
        {
            // 변환 행렬 재계산
            config.CalibratedTransform = _calibrationService.CalculateAffineTransform(config.CalibrationPoints);

            if (config.CalibratedTransform != null)
            {
                StatusUpdated?.Invoke($"Calibration saved! ({config.CalibrationPoints.Count} points)");

                // 설정 저장
                _trackerService.SaveSettings();

                // 마커 새로고침 요청
                CalibrationCompleted?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                StatusUpdated?.Invoke("Calibration calculation failed.");
            }
        }
        else
        {
            _trackerService.SaveSettings();
        }
    }

    /// <summary>
    /// 탈출구 마커에 캘리브레이션 이벤트 설정
    /// </summary>
    /// <param name="marker">탈출구 마커 UI 요소</param>
    /// <param name="extract">탈출구 데이터</param>
    public void SetupMarkerForCalibration(FrameworkElement marker, MapExtract extract)
    {
        marker.Tag = extract;
        marker.Cursor = Cursors.SizeAll;
        marker.MouseLeftButtonDown += ExtractMarker_CalibrationMouseDown;
        marker.MouseMove += ExtractMarker_CalibrationMouseMove;
        marker.MouseLeftButtonUp += ExtractMarker_CalibrationMouseUp;
    }

    #endregion

    #region Private Methods - Mouse Event Handlers

    private void ExtractMarker_CalibrationMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isCalibrationMode) return;
        if (sender is not FrameworkElement marker) return;

        _draggingExtractMarker = marker;
        _draggingExtract = marker.Tag as MapExtract;
        _extractDragStartPoint = e.GetPosition(_extractMarkersContainer);
        _extractMarkerOriginalLeft = Canvas.GetLeft(marker);
        _extractMarkerOriginalTop = Canvas.GetTop(marker);

        marker.CaptureMouse();
        e.Handled = true;
    }

    private void ExtractMarker_CalibrationMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isCalibrationMode || _draggingExtractMarker == null) return;

        var currentPoint = e.GetPosition(_extractMarkersContainer);
        var deltaX = currentPoint.X - _extractDragStartPoint.X;
        var deltaY = currentPoint.Y - _extractDragStartPoint.Y;

        Canvas.SetLeft(_draggingExtractMarker, _extractMarkerOriginalLeft + deltaX);
        Canvas.SetTop(_draggingExtractMarker, _extractMarkerOriginalTop + deltaY);
    }

    private void ExtractMarker_CalibrationMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isCalibrationMode || _draggingExtractMarker == null || _draggingExtract == null) return;

        _draggingExtractMarker.ReleaseMouseCapture();

        // 최종 위치 계산 (마커 중심 기준)
        var finalLeft = Canvas.GetLeft(_draggingExtractMarker);
        var finalTop = Canvas.GetTop(_draggingExtractMarker);

        // 마커 크기의 절반을 더해 중심점 계산
        var markerWidth = _draggingExtractMarker.ActualWidth > 0 ? _draggingExtractMarker.ActualWidth : 20;
        var markerHeight = _draggingExtractMarker.ActualHeight > 0 ? _draggingExtractMarker.ActualHeight : 20;
        var centerX = finalLeft + markerWidth / 2;
        var centerY = finalTop + markerHeight / 2;

        // 보정 포인트 추가
        var config = _trackerService?.GetMapConfig(_currentMapKey ?? "");
        if (config != null)
        {
            var calibrationPoint = new CalibrationPoint
            {
                Id = _draggingExtract.Id,
                Name = _draggingExtract.Name,
                GameX = _draggingExtract.X,
                GameZ = _draggingExtract.Z,
                ScreenX = centerX,
                ScreenY = centerY
            };

            var hasEnough = _calibrationService.AddCalibrationPoint(config, calibrationPoint);
            var pointCount = config.CalibrationPoints?.Count ?? 0;

            var statusMessage = $"Calibration point set: {_draggingExtract.Name} ({pointCount} points)";
            if (pointCount >= 3)
            {
                statusMessage += " - Ready to apply!";
            }
            else
            {
                statusMessage += $" - Need {3 - pointCount} more points";
            }
            StatusUpdated?.Invoke(statusMessage);
        }

        _draggingExtractMarker = null;
        _draggingExtract = null;
        e.Handled = true;
    }

    #endregion
}
