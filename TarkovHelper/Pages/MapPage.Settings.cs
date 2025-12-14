using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TarkovHelper.Pages;

/// <summary>
/// Map Page - Settings and UI Controls partial class
/// </summary>
public partial class MapPage : UserControl
{
    #region UI Controls (Legend, Copy, Keyboard Shortcuts)

    /// <summary>
    /// 페이지 전역 키보드 단축키 처리
    /// </summary>
    private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 텍스트 입력 중이면 단축키 무시
        if (e.OriginalSource is TextBox) return;

        switch (e.Key)
        {
            case Key.D1: // 1 - 탈출구 토글
            case Key.E:
                ChkShowMarkers.IsChecked = !ChkShowMarkers.IsChecked;
                e.Handled = true;
                break;

            case Key.D2: // 2 - 환승 토글
            case Key.T:
                ChkShowTransit.IsChecked = !ChkShowTransit.IsChecked;
                e.Handled = true;
                break;

            case Key.D3: // 3 - 퀘스트 토글
                ChkShowQuests.IsChecked = !ChkShowQuests.IsChecked;
                e.Handled = true;
                break;

            case Key.R: // R - 뷰 초기화
                BtnResetView_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.Q: // Q - Drawer 토글
                BtnToggleDrawer_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.L: // L - 범례 토글
                BtnLegend_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.Escape: // ESC - 팝업 닫기
                if (LegendPopup.Visibility == Visibility.Visible)
                {
                    LegendPopup.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                }
                else if (_isDrawerOpen)
                {
                    CloseDrawer();
                    e.Handled = true;
                }
                break;

            case Key.OemPlus: // + - 줌 인
            case Key.Add:
                BtnZoomIn_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.OemMinus: // - - 줌 아웃
            case Key.Subtract:
                BtnZoomOut_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            // 넘패드 0~4: 층 변경
            case Key.NumPad0:
                SelectFloorByIndex(0);
                e.Handled = true;
                break;
            case Key.NumPad1:
                SelectFloorByIndex(1);
                e.Handled = true;
                break;
            case Key.NumPad2:
                SelectFloorByIndex(2);
                e.Handled = true;
                break;
            case Key.NumPad3:
                SelectFloorByIndex(3);
                e.Handled = true;
                break;
            case Key.NumPad4:
                SelectFloorByIndex(4);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// 인덱스로 층 선택 (0부터 시작)
    /// </summary>
    private void SelectFloorByIndex(int index)
    {
        if (_sortedFloors == null || _sortedFloors.Count == 0)
        {
            StatusText.Text = "이 맵에는 층이 없습니다";
            return;
        }

        if (index >= 0 && index < FloorSelector.Items.Count)
        {
            FloorSelector.SelectedIndex = index;
            var floorName = _sortedFloors[index].DisplayName;
            StatusText.Text = $"층 변경: {floorName}";
        }
        else
        {
            StatusText.Text = $"층 {index}이(가) 없습니다 (0~{FloorSelector.Items.Count - 1} 사용 가능)";
        }
    }

    /// <summary>
    /// 범례 버튼 클릭 - 범례 표시/숨김 토글
    /// </summary>
    private void BtnLegend_Click(object sender, RoutedEventArgs e)
    {
        if (LegendPopup.Visibility == Visibility.Visible)
        {
            LegendPopup.Visibility = Visibility.Collapsed;
        }
        else
        {
            LegendPopup.Visibility = Visibility.Visible;
            // 다른 팝업 닫기
            SettingsPopup.Visibility = Visibility.Collapsed;
            QuestFilterPopup.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 범례 닫기 버튼 클릭
    /// </summary>
    private void BtnCloseLegend_Click(object sender, RoutedEventArgs e)
    {
        LegendPopup.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 퀘스트 타입 필터 버튼 클릭
    /// </summary>
    private void BtnQuestFilter_Click(object sender, RoutedEventArgs e)
    {
        if (QuestFilterPopup.Visibility == Visibility.Visible)
        {
            QuestFilterPopup.Visibility = Visibility.Collapsed;
        }
        else
        {
            QuestFilterPopup.Visibility = Visibility.Visible;
            // 다른 팝업 닫기
            LegendPopup.Visibility = Visibility.Collapsed;
            SettingsPopup.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 퀘스트 타입 필터 닫기 버튼 클릭
    /// </summary>
    private void BtnCloseQuestFilter_Click(object sender, RoutedEventArgs e)
    {
        QuestFilterPopup.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 퀘스트 타입 필터 체크박스 변경
    /// </summary>
    private void QuestTypeFilter_Changed(object sender, RoutedEventArgs e)
    {
        UpdateEnabledQuestTypes();
        RefreshQuestMarkers();

        // Update drawer if open
        if (_isDrawerOpen)
        {
            RefreshQuestDrawer();
        }
    }

    /// <summary>
    /// 전체 선택 버튼 클릭
    /// </summary>
    private void BtnSelectAllFilters_Click(object sender, RoutedEventArgs e)
    {
        SetAllQuestFilters(true);
    }

    /// <summary>
    /// 전체 해제 버튼 클릭
    /// </summary>
    private void BtnClearAllFilters_Click(object sender, RoutedEventArgs e)
    {
        SetAllQuestFilters(false);
    }

    /// <summary>
    /// 설정 버튼 클릭 - 설정 팝업 표시/숨김 토글
    /// </summary>
    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsPopup.Visibility == Visibility.Visible)
        {
            SettingsPopup.Visibility = Visibility.Collapsed;
        }
        else
        {
            SettingsPopup.Visibility = Visibility.Visible;
            // 다른 팝업 닫기
            LegendPopup.Visibility = Visibility.Collapsed;
            QuestFilterPopup.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 설정 닫기 버튼 클릭
    /// </summary>
    private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsPopup.Visibility = Visibility.Collapsed;
    }

    #region Settings Panel - Tab Navigation

    /// <summary>
    /// 설정 탭 클릭 - 탭 전환
    /// </summary>
    private void SettingsTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton tab) return;

        var tabName = tab.Tag?.ToString();
        PanelDisplay.Visibility = tabName == "Display" ? Visibility.Visible : Visibility.Collapsed;
        PanelMarker.Visibility = tabName == "Marker" ? Visibility.Visible : Visibility.Collapsed;
        PanelTracker.Visibility = tabName == "Tracker" ? Visibility.Visible : Visibility.Collapsed;
        PanelShortcuts.Visibility = tabName == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region Settings Panel - Display Tab

    /// <summary>
    /// 이동 경로 표시/숨김
    /// </summary>
    private void TrailVisibility_Changed(object sender, RoutedEventArgs e)
    {
        _showTrail = ChkShowTrail?.IsChecked ?? true;
        TrailCanvas.Visibility = _showTrail ? Visibility.Visible : Visibility.Collapsed;
        _settings.MapShowTrail = _showTrail;
    }

    /// <summary>
    /// 미니맵 표시/숨김
    /// </summary>
    private void MinimapVisibility_Changed(object sender, RoutedEventArgs e)
    {
        var show = ChkShowMinimap?.IsChecked ?? true;
        MinimapContainer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _settings.MapShowMinimap = show;
    }

    /// <summary>
    /// 미니맵 크기 변경
    /// </summary>
    private void MinimapSize_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radio) return;

        var size = radio.Tag?.ToString() ?? "Medium";
        _settings.MapMinimapSize = size;

        // UpdateMinimap()이 맵 비율에 맞게 동적으로 크기 계산
        UpdateMinimap();
    }

    #endregion

    #region Settings Panel - Marker Tab

    /// <summary>
    /// 마커 크기 프리셋 버튼 클릭
    /// </summary>
    private void MarkerScalePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        if (double.TryParse(btn.Tag?.ToString(), out var scale))
        {
            MarkerScaleSlider.Value = scale;
        }
    }

    /// <summary>
    /// 마커 투명도 슬라이더 변경
    /// </summary>
    private void MarkerOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MarkerOpacityText == null) return;

        var opacity = e.NewValue;
        MarkerOpacityText.Text = $"{(int)(opacity * 100)}%";

        // Apply opacity to marker canvases
        MarkersCanvas.Opacity = opacity;
        TransitCanvas.Opacity = opacity;
        QuestMarkersCanvas.Opacity = opacity;

        _settings.MapMarkerOpacity = opacity;
    }

    /// <summary>
    /// 완료 퀘스트 자동 숨김 변경
    /// </summary>
    private void AutoHideCompleted_Changed(object sender, RoutedEventArgs e)
    {
        var autoHide = ChkAutoHideCompleted?.IsChecked ?? false;
        _settings.MapAutoHideCompleted = autoHide;
        RefreshQuestMarkers();
        if (_isDrawerOpen) RefreshQuestDrawer();
    }

    /// <summary>
    /// 완료 퀘스트 흐리게 표시 변경
    /// </summary>
    private void FadeCompleted_Changed(object sender, RoutedEventArgs e)
    {
        var fade = ChkFadeCompleted?.IsChecked ?? true;
        _settings.MapFadeCompleted = fade;
        RefreshQuestMarkers();
    }

    /// <summary>
    /// 마커 라벨 표시 변경
    /// </summary>
    private void ShowLabels_Changed(object sender, RoutedEventArgs e)
    {
        var showLabels = ChkShowLabels?.IsChecked ?? true;
        _settings.MapShowLabels = showLabels;
        RedrawMarkers();
        RedrawTransit();
        RefreshQuestMarkers();
    }

    #endregion

    #region Settings Panel - Tracker Tab

    /// <summary>
    /// 폴더 열기 버튼 클릭
    /// </summary>
    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = _trackerService.Settings.ScreenshotFolderPath;
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", folder);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"폴더 열기 실패: {ex.Message}";
            }
        }
        else
        {
            StatusText.Text = "폴더가 설정되지 않았습니다.";
        }
    }

    /// <summary>
    /// 경로 색상 변경
    /// </summary>
    private void TrailColor_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radio) return;

        var colorHex = radio.Tag?.ToString();
        if (string.IsNullOrEmpty(colorHex)) return;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorHex);
            _settings.MapTrailColor = colorHex;

            // 기존 트레일 마커 색상 업데이트
            foreach (var marker in _trailMarkers)
            {
                marker.Fill = new SolidColorBrush(color);
            }
        }
        catch { }
    }

    /// <summary>
    /// 경로 두께 변경
    /// </summary>
    private void TrailThickness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TrailThicknessText == null) return;

        var thickness = (int)e.NewValue;
        TrailThicknessText.Text = $"{thickness}px";
        _settings.MapTrailThickness = thickness;

        // 기존 트레일 마커 크기 업데이트
        foreach (var marker in _trailMarkers)
        {
            marker.Width = thickness * 2;
            marker.Height = thickness * 2;
        }
    }

    /// <summary>
    /// 자동 추적 시작 옵션 변경
    /// </summary>
    private void AutoStartTracking_Changed(object sender, RoutedEventArgs e)
    {
        var autoStart = ChkAutoStartTracking?.IsChecked ?? false;
        _settings.MapAutoStartTracking = autoStart;
    }

    /// <summary>
    /// 트래킹 폴더 텍스트 업데이트
    /// </summary>
    private void UpdateTrackingFolderText()
    {
        if (TrackingFolderText == null) return;

        var folder = _trackerService.Settings.ScreenshotFolderPath;
        if (string.IsNullOrEmpty(folder))
        {
            TrackingFolderText.Text = "폴더 미선택";
        }
        else
        {
            // 경로가 너무 길면 축약
            var displayPath = folder.Length > 40 ? "..." + folder.Substring(folder.Length - 37) : folder;
            TrackingFolderText.Text = displayPath;
        }
    }

    #endregion

    #region Settings Panel - Reset

    /// <summary>
    /// 설정 초기화 버튼 클릭
    /// </summary>
    private void BtnResetSettings_Click(object sender, RoutedEventArgs e)
    {
        // 레이어 표시 초기화
        if (ChkShowMarkers != null) ChkShowMarkers.IsChecked = true;
        if (ChkShowTransit != null) ChkShowTransit.IsChecked = true;
        if (ChkShowQuests != null) ChkShowQuests.IsChecked = true;
        if (ChkShowTrail != null) ChkShowTrail.IsChecked = true;
        if (ChkShowMinimap != null) ChkShowMinimap.IsChecked = true;

        // 마커 설정 초기화
        if (MarkerScaleSlider != null) MarkerScaleSlider.Value = 1.0;
        if (MarkerOpacitySlider != null) MarkerOpacitySlider.Value = 1.0;
        if (ChkAutoHideCompleted != null) ChkAutoHideCompleted.IsChecked = false;
        if (ChkFadeCompleted != null) ChkFadeCompleted.IsChecked = true;
        if (ChkShowLabels != null) ChkShowLabels.IsChecked = true;

        // 미니맵 크기 초기화
        if (MinimapSizeM != null) MinimapSizeM.IsChecked = true;
        MinimapContent.Width = 180;
        MinimapContent.Height = 140;

        // 트래커 설정 초기화
        if (TrailColorBlue != null) TrailColorBlue.IsChecked = true;
        if (TrailThicknessSlider != null) TrailThicknessSlider.Value = 2;
        if (ChkAutoStartTracking != null) ChkAutoStartTracking.IsChecked = false;

        // 설정 저장
        _settings.MapShowExtracts = true;
        _settings.MapShowTransits = true;
        _settings.MapShowQuests = true;
        _settings.MapShowTrail = true;
        _settings.MapShowMinimap = true;
        _settings.MapMarkerScale = 1.0;
        _settings.MapMarkerOpacity = 1.0;
        _settings.MapAutoHideCompleted = false;
        _settings.MapFadeCompleted = true;
        _settings.MapShowLabels = true;
        _settings.MapMinimapSize = "Medium";
        _settings.MapTrailColor = "#2196F3";
        _settings.MapTrailThickness = 2;
        _settings.MapAutoStartTracking = false;

        // UI 갱신
        _markerScale = 1.0;
        RedrawMarkers();
        RedrawTransit();
        RefreshQuestMarkers();
        UpdateMinimap();

        StatusText.Text = "설정이 초기화되었습니다.";
    }

    #endregion

    /// <summary>
    /// 모든 퀘스트 필터 설정
    /// </summary>
    private void SetAllQuestFilters(bool isChecked)
    {
        if (ChkFilterVisit != null) ChkFilterVisit.IsChecked = isChecked;
        if (ChkFilterMark != null) ChkFilterMark.IsChecked = isChecked;
        if (ChkFilterPlant != null) ChkFilterPlant.IsChecked = isChecked;
        if (ChkFilterExtract != null) ChkFilterExtract.IsChecked = isChecked;
        if (ChkFilterFind != null) ChkFilterFind.IsChecked = isChecked;
        if (ChkFilterKill != null) ChkFilterKill.IsChecked = isChecked;
        if (ChkFilterOther != null) ChkFilterOther.IsChecked = isChecked;

        UpdateEnabledQuestTypes();
        RefreshQuestMarkers();

        if (_isDrawerOpen)
        {
            RefreshQuestDrawer();
        }
    }

    /// <summary>
    /// 활성화된 퀘스트 타입 업데이트
    /// </summary>
    private void UpdateEnabledQuestTypes()
    {
        _enabledQuestTypes.Clear();

        if (ChkFilterVisit?.IsChecked == true) _enabledQuestTypes.Add("visit");
        if (ChkFilterMark?.IsChecked == true) _enabledQuestTypes.Add("mark");
        if (ChkFilterPlant?.IsChecked == true) _enabledQuestTypes.Add("plantitem");
        if (ChkFilterExtract?.IsChecked == true) _enabledQuestTypes.Add("extract");
        if (ChkFilterFind?.IsChecked == true)
        {
            _enabledQuestTypes.Add("finditem");
            _enabledQuestTypes.Add("findquestitem");
            _enabledQuestTypes.Add("giveitem");
        }
        if (ChkFilterKill?.IsChecked == true)
        {
            _enabledQuestTypes.Add("kill");
            _enabledQuestTypes.Add("shoot");
        }
        if (ChkFilterOther?.IsChecked == true) _enabledQuestTypes.Add("other");
    }

    /// <summary>
    /// 퀘스트 타입이 필터에 의해 표시되어야 하는지 확인
    /// </summary>
    private bool IsQuestTypeEnabled(string? type)
    {
        if (string.IsNullOrEmpty(type)) return _enabledQuestTypes.Contains("other");

        var normalizedType = type.ToLowerInvariant();

        // 직접 매칭
        if (_enabledQuestTypes.Contains(normalizedType)) return true;

        // 특수 케이스 처리
        if (normalizedType.Contains("find") || normalizedType.Contains("give"))
            return _enabledQuestTypes.Contains("finditem");
        if (normalizedType.Contains("kill") || normalizedType.Contains("shoot"))
            return _enabledQuestTypes.Contains("kill");

        // 알 수 없는 타입은 "other"로 처리
        return _enabledQuestTypes.Contains("other");
    }

    /// <summary>
    /// 좌표 복사 버튼 클릭 - 현재 마우스 위치 게임 좌표를 클립보드에 복사
    /// </summary>
    private void BtnCopyCoords_Click(object sender, RoutedEventArgs e)
    {
        if (_hasValidCoordinates)
        {
            var coordsText = $"X: {_currentGameX:F1}, Z: {_currentGameZ:F1}";
            try
            {
                Clipboard.SetText(coordsText);
                StatusText.Text = $"좌표 복사됨: {coordsText}";
            }
            catch
            {
                StatusText.Text = "좌표 복사 실패";
            }
        }
        else
        {
            StatusText.Text = "복사할 좌표 없음 - 맵 위에 마우스를 올려주세요";
        }
    }

    /// <summary>
    /// 상태바 맵 정보 업데이트
    /// </summary>
    private void UpdateStatusBarMapInfo()
    {
        if (_currentMapConfig != null)
        {
            CurrentMapName.Text = _currentMapConfig.DisplayName;

            if (_sortedFloors != null && _sortedFloors.Count > 0 && !string.IsNullOrEmpty(_currentFloorId))
            {
                var floor = _sortedFloors.FirstOrDefault(f => f.LayerId == _currentFloorId);
                CurrentFloorName.Text = floor != null ? $"[{floor.DisplayName}]" : "";
            }
            else
            {
                CurrentFloorName.Text = "";
            }
        }
        else
        {
            CurrentMapName.Text = "맵 선택";
            CurrentFloorName.Text = "";
        }
    }

    #endregion
}
