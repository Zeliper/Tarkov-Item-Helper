using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TarkovHelper.Models.MapTracker;
using TarkovHelper.Services.MapTracker;

namespace TarkovHelper.Pages;

/// <summary>
/// Map Page - Quest Drawer partial class
/// </summary>
public partial class MapPage : UserControl
{
    #region Quest Drawer

    /// <summary>
    /// Drawer 토글 버튼 클릭
    /// </summary>
    private void BtnToggleDrawer_Click(object sender, RoutedEventArgs e)
    {
        if (_isDrawerOpen)
        {
            CloseDrawer();
        }
        else
        {
            OpenDrawer();
        }
    }

    /// <summary>
    /// Drawer 닫기 버튼 클릭
    /// </summary>
    private void BtnCloseDrawer_Click(object sender, RoutedEventArgs e)
    {
        CloseDrawer();
    }

    /// <summary>
    /// Drawer 열기
    /// </summary>
    private void OpenDrawer()
    {
        _isDrawerOpen = true;
        QuestDrawerColumn.Width = new GridLength(_drawerWidth);
        QuestDrawerPanel.Visibility = Visibility.Visible;
        BtnToggleDrawer.Content = "▶";
        RefreshQuestDrawer();

        // Save drawer state
        _settings.MapDrawerOpen = true;
    }

    /// <summary>
    /// Drawer 닫기
    /// </summary>
    private void CloseDrawer()
    {
        _isDrawerOpen = false;
        QuestDrawerColumn.Width = new GridLength(0);
        QuestDrawerPanel.Visibility = Visibility.Collapsed;
        BtnToggleDrawer.Content = "◀";

        // Save drawer state
        _settings.MapDrawerOpen = false;
    }

    /// <summary>
    /// Drawer 필터 변경
    /// </summary>
    private void DrawerFilter_Changed(object sender, RoutedEventArgs e)
    {
        // Save filter states to settings
        if (sender is CheckBox checkBox)
        {
            var isChecked = checkBox.IsChecked == true;
            switch (checkBox.Name)
            {
                case "ChkIncompleteOnly":
                    _settings.MapIncompleteOnly = isChecked;
                    break;
                case "ChkCurrentMapOnly":
                    _settings.MapCurrentMapOnly = isChecked;
                    break;
            }
        }

        if (_isDrawerOpen)
        {
            RefreshQuestDrawer();
        }
    }

    /// <summary>
    /// Drawer 퀘스트 목록 새로고침
    /// </summary>
    private void RefreshQuestDrawer()
    {
        // 초기화 전 또는 서비스가 준비되지 않은 경우 무시
        if (_dbService == null || !_dbService.ObjectivesLoaded) return;

        var incompleteOnly = ChkIncompleteOnly?.IsChecked == true;
        var currentMapOnly = ChkCurrentMapOnly?.IsChecked == true;

        // DB에서 모든 퀘스트 목표 가져와서 변환
        var allDbObjectives = _dbService.GetAllObjectives();
        var allObjectives = allDbObjectives.Select(ConvertToTaskObjective).ToList();

        // 필터 적용
        var filteredObjectives = allObjectives
            .Where(obj =>
            {
                // 미완료 필터
                if (incompleteOnly)
                {
                    var isCompleted = _progressService.IsQuestCompleted(obj.TaskNormalizedName);
                    if (isCompleted) return false;
                }

                // 현재 맵 필터
                if (currentMapOnly && _currentMapConfig != null)
                {
                    var mapKey = _currentMapConfig.Key;
                    var normalizedMapKey = MapMarkerDbService.NormalizeMapKey(mapKey);
                    var hasLocationOnMap = obj.Locations.Any(loc =>
                    {
                        var locationMapNormalized = MapMarkerDbService.NormalizeMapKey(loc.MapName ?? "");
                        return locationMapNormalized.Equals(normalizedMapKey, StringComparison.OrdinalIgnoreCase);
                    });
                    if (!hasLocationOnMap) return false;
                }

                // 위치 정보가 있는 목표만
                return obj.Locations.Count > 0;
            })
            .ToList();

        // 퀘스트별로 그룹화
        var groups = filteredObjectives
            .GroupBy(obj => obj.TaskNormalizedName)
            .Select(g =>
            {
                var firstObj = g.First();
                var questName = !string.IsNullOrEmpty(firstObj.TaskNameKo)
                    ? firstObj.TaskNameKo
                    : firstObj.TaskName;

                // 각 목표별로 완료 상태 확인
                var objectives = g.Select(obj =>
                {
                    var objCompleted = _progressService.IsObjectiveCompletedById(obj.ObjectiveId);
                    return new QuestDrawerItem(obj, objCompleted);
                }).ToList();

                // 퀘스트 완료 = 모든 목표 완료
                var isQuestCompleted = objectives.All(o => o.IsCompleted);

                var group = new QuestDrawerGroup(g.Key, questName, isQuestCompleted, objectives)
                {
                    IsVisible = !_hiddenQuestIds.Contains(g.Key),
                    IsHighlighted = g.Key == _highlightedQuestId,
                    IsExpanded = !_collapsedQuestIds.Contains(g.Key)
                };
                return group;
            })
            .ToList();

        // 검색 필터 적용
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var search = _searchText.ToLowerInvariant();
            groups = groups.Where(g =>
                g.QuestName.ToLowerInvariant().Contains(search) ||
                g.Objectives.Any(o => o.DescriptionDisplay.ToLowerInvariant().Contains(search))
            ).ToList();
        }

        // 정렬 적용
        groups = _sortOption switch
        {
            "progress" => groups.OrderByDescending(g => (double)g.CompletedCount / g.ObjectiveCount)
                                .ThenBy(g => g.QuestName).ToList(),
            "count" => groups.OrderByDescending(g => g.ObjectiveCount)
                             .ThenBy(g => g.QuestName).ToList(),
            _ => groups.OrderBy(g => g.QuestName).ToList() // name (default)
        };

        QuestObjectivesList.ItemsSource = groups;

        // 빈 상태 패널 표시/숨김
        if (EmptyStatePanel != null)
        {
            EmptyStatePanel.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // 퀘스트 카운트 업데이트
        var totalObjectives = groups.Sum(g => g.ObjectiveCount);
        var completedObjectives = groups.Sum(g => g.CompletedCount);
        if (QuestCountText != null)
        {
            QuestCountText.Text = $"({groups.Count}퀘스트, {completedObjectives}/{totalObjectives})";
        }

        // 헤더 퀘스트 카운트 업데이트
        if (HeaderQuestCount != null)
        {
            var incompleteCount = groups.Count(g => !g.IsCompleted);
            HeaderQuestCount.Text = incompleteCount > 0 ? $"({incompleteCount})" : "";
        }
    }

    /// <summary>
    /// Drawer 퀘스트 아이템 클릭 - 맵에서 해당 위치로 포커스
    /// </summary>
    private void QuestDrawerItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is QuestDrawerItem item)
        {
            FocusOnQuestObjective(item.Objective);
        }
    }

    /// <summary>
    /// 퀘스트 표시/숨김 토글
    /// </summary>
    private void QuestVisibilityToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.DataContext is QuestDrawerGroup group)
        {
            if (checkBox.IsChecked == true)
            {
                _hiddenQuestIds.Remove(group.QuestId);
            }
            else
            {
                _hiddenQuestIds.Add(group.QuestId);
            }

            // Save to settings
            _settings.MapHiddenQuests = _hiddenQuestIds;

            // 맵 마커 새로고침
            RefreshQuestMarkers();
        }
    }

    /// <summary>
    /// 퀘스트 목표 우클릭 - 완료 토글
    /// </summary>
    private void QuestDrawerItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is QuestDrawerItem item)
        {
            var objectiveId = item.Objective.ObjectiveId;
            var questId = item.Objective.TaskNormalizedName;
            var isCurrentlyCompleted = _progressService.IsObjectiveCompletedById(objectiveId);

            // 완료 상태 토글
            _progressService.SetObjectiveCompletedById(objectiveId, !isCurrentlyCompleted, questId);

            // UI 새로고침
            RefreshQuestDrawer();
            RefreshQuestMarkers();

            e.Handled = true;
        }
    }

    /// <summary>
    /// 퀘스트 헤더 클릭 - 접기/펼치기
    /// </summary>
    private void QuestHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is QuestDrawerGroup group)
        {
            group.IsExpanded = !group.IsExpanded;
            if (group.IsExpanded)
                _collapsedQuestIds.Remove(group.QuestId);
            else
                _collapsedQuestIds.Add(group.QuestId);

            // Save to settings
            _settings.MapCollapsedQuests = _collapsedQuestIds;
        }
    }

    /// <summary>
    /// 검색어 변경
    /// </summary>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            _searchText = textBox.Text;
            RefreshQuestDrawer();
        }
    }

    /// <summary>
    /// 정렬 옵션 변경
    /// </summary>
    private void SortOption_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
        {
            _sortOption = item.Tag?.ToString() ?? "name";
            _settings.MapSortOption = _sortOption; // Save to settings
            RefreshQuestDrawer();
        }
    }

    /// <summary>
    /// 전체 표시 버튼
    /// </summary>
    private void BtnShowAll_Click(object sender, RoutedEventArgs e)
    {
        _hiddenQuestIds.Clear();
        _settings.MapHiddenQuests = _hiddenQuestIds; // Save to settings
        RefreshQuestDrawer();
        RefreshQuestMarkers();
    }

    /// <summary>
    /// 전체 숨김 버튼
    /// </summary>
    private void BtnHideAll_Click(object sender, RoutedEventArgs e)
    {
        if (QuestObjectivesList.ItemsSource is List<QuestDrawerGroup> groups)
        {
            foreach (var g in groups)
                _hiddenQuestIds.Add(g.QuestId);
            _settings.MapHiddenQuests = _hiddenQuestIds; // Save to settings
            RefreshQuestDrawer();
            RefreshQuestMarkers();
        }
    }

    /// <summary>
    /// 전체 펼치기
    /// </summary>
    private void BtnExpandAll_Click(object sender, RoutedEventArgs e)
    {
        _collapsedQuestIds.Clear();
        _settings.MapCollapsedQuests = _collapsedQuestIds; // Save to settings
        RefreshQuestDrawer();
    }

    /// <summary>
    /// 전체 접기
    /// </summary>
    private void BtnCollapseAll_Click(object sender, RoutedEventArgs e)
    {
        if (QuestObjectivesList.ItemsSource is List<QuestDrawerGroup> groups)
        {
            foreach (var g in groups)
                _collapsedQuestIds.Add(g.QuestId);
            _settings.MapCollapsedQuests = _collapsedQuestIds; // Save to settings
            RefreshQuestDrawer();
        }
    }

    /// <summary>
    /// 도움말 버튼 클릭 - 키보드 힌트 토글
    /// </summary>
    private void BtnDrawerHelp_Click(object sender, RoutedEventArgs e)
    {
        if (DrawerHelpPanel != null)
        {
            DrawerHelpPanel.Visibility = DrawerHelpPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    /// <summary>
    /// 옵션 버튼 클릭 - 빠른 작업 팝업
    /// </summary>
    private void BtnDrawerOptions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        var menu = new ContextMenu();

        var showAllItem = new MenuItem { Header = "전체 표시" };
        showAllItem.Click += BtnShowAll_Click;
        menu.Items.Add(showAllItem);

        var hideAllItem = new MenuItem { Header = "전체 숨김" };
        hideAllItem.Click += BtnHideAll_Click;
        menu.Items.Add(hideAllItem);

        menu.Items.Add(new Separator());

        var expandAllItem = new MenuItem { Header = "모두 펼치기" };
        expandAllItem.Click += BtnExpandAll_Click;
        menu.Items.Add(expandAllItem);

        var collapseAllItem = new MenuItem { Header = "모두 접기" };
        collapseAllItem.Click += BtnCollapseAll_Click;
        menu.Items.Add(collapseAllItem);

        menu.PlacementTarget = btn;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>
    /// 목표 맵 이동 버튼 클릭
    /// </summary>
    private void ObjectiveGoToMap_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is QuestDrawerItem item)
        {
            FocusOnQuestObjective(item.Objective);
        }
        e.Handled = true;
    }

    /// <summary>
    /// 컨텍스트 메뉴 - 맵에서 숨기기
    /// </summary>
    private void QuestContextMenu_HideFromMap(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is QuestDrawerGroup group)
        {
            _hiddenQuestIds.Add(group.QuestId);
            _settings.MapHiddenQuests = _hiddenQuestIds;
            RefreshQuestDrawer();
            RefreshQuestMarkers();
        }
    }

    /// <summary>
    /// 퀘스트 컨텍스트 메뉴 - 모두 완료
    /// </summary>
    private void QuestContextMenu_MarkAllComplete(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is QuestDrawerGroup group)
        {
            foreach (var obj in group.Objectives)
            {
                _progressService.SetObjectiveCompletedById(obj.Objective.ObjectiveId, true, group.QuestId);
            }
            RefreshQuestDrawer();
            RefreshQuestMarkers();
        }
    }

    /// <summary>
    /// 퀘스트 컨텍스트 메뉴 - 모두 미완료
    /// </summary>
    private void QuestContextMenu_MarkAllIncomplete(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is QuestDrawerGroup group)
        {
            foreach (var obj in group.Objectives)
            {
                _progressService.SetObjectiveCompletedById(obj.Objective.ObjectiveId, false, group.QuestId);
            }
            RefreshQuestDrawer();
            RefreshQuestMarkers();
        }
    }

    /// <summary>
    /// Drawer 리사이즈 시작
    /// </summary>
    private void DrawerResizeGrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement grip)
        {
            grip.CaptureMouse();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Drawer 리사이즈 진행
    /// </summary>
    private void DrawerResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement grip && grip.IsMouseCaptured)
        {
            var pos = e.GetPosition(this);
            var newWidth = pos.X;
            if (newWidth >= DrawerMinWidth && newWidth <= DrawerMaxWidth)
            {
                _drawerWidth = newWidth;
                QuestDrawerColumn.Width = new GridLength(_drawerWidth);
            }
        }
    }

    /// <summary>
    /// Drawer 리사이즈 종료
    /// </summary>
    private void DrawerResizeGrip_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement grip)
        {
            grip.ReleaseMouseCapture();
        }
    }

    /// <summary>
    /// Drawer 키보드 네비게이션
    /// </summary>
    private void QuestDrawer_KeyDown(object sender, KeyEventArgs e)
    {
        if (QuestObjectivesList.ItemsSource is not List<QuestDrawerGroup> groups || groups.Count == 0)
            return;

        var currentIndex = -1;
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i].IsHighlighted)
            {
                currentIndex = i;
                break;
            }
        }

        switch (e.Key)
        {
            case Key.Down:
                currentIndex = Math.Min(currentIndex + 1, groups.Count - 1);
                break;
            case Key.Up:
                currentIndex = Math.Max(currentIndex - 1, 0);
                break;
            case Key.Enter when currentIndex >= 0:
                // 첫 번째 목표로 포커스
                var firstObj = groups[currentIndex].Objectives.FirstOrDefault();
                if (firstObj != null)
                    FocusOnQuestObjective(firstObj.Objective);
                return;
            case Key.Space when currentIndex >= 0:
                // 접기/펼치기 토글
                groups[currentIndex].IsExpanded = !groups[currentIndex].IsExpanded;
                if (groups[currentIndex].IsExpanded)
                    _collapsedQuestIds.Remove(groups[currentIndex].QuestId);
                else
                    _collapsedQuestIds.Add(groups[currentIndex].QuestId);
                _settings.MapCollapsedQuests = _collapsedQuestIds; // Save to settings
                return;
            default:
                return;
        }

        // 하이라이트 업데이트
        _highlightedQuestId = currentIndex >= 0 ? groups[currentIndex].QuestId : null;
        RefreshQuestDrawer();
        e.Handled = true;
    }

    /// <summary>
    /// 퀘스트 목표 위치로 맵 포커스 이동 (부드러운 애니메이션)
    /// </summary>
    private void FocusOnQuestObjective(TaskObjectiveWithLocation objective)
    {
        if (_currentMapConfig == null) return;

        // 현재 맵의 위치 찾기
        var mapKey = _currentMapConfig.Key.ToLowerInvariant();
        var locationsOnMap = objective.Locations
            .Where(loc =>
            {
                var locationMapName = loc.MapNormalizedName?.ToLowerInvariant() ?? "";
                var locationMapNameAlt = loc.MapName?.ToLowerInvariant() ?? "";
                return locationMapName == mapKey || locationMapNameAlt == mapKey;
            })
            .ToList();

        if (locationsOnMap.Count == 0)
        {
            // 현재 맵에 위치가 없으면 해당 맵으로 전환
            var firstLocation = objective.Locations.FirstOrDefault();
            if (firstLocation != null)
            {
                var targetMapKey = firstLocation.MapNormalizedName ?? firstLocation.MapName;
                if (!string.IsNullOrEmpty(targetMapKey))
                {
                    // 맵 선택기에서 해당 맵 찾기
                    for (int i = 0; i < MapSelector.Items.Count; i++)
                    {
                        if (MapSelector.Items[i] is DbMapConfig config &&
                            config.Key.Equals(targetMapKey, StringComparison.OrdinalIgnoreCase))
                        {
                            MapSelector.SelectedIndex = i;
                            // 맵 변경 후 다시 포커스 시도
                            Dispatcher.BeginInvoke(new Action(() => FocusOnQuestObjective(objective)),
                                System.Windows.Threading.DispatcherPriority.Loaded);
                            return;
                        }
                    }
                }
            }
            return;
        }

        // Centroid 계산 (tarkov.dev API: X=horizontal X, Z=horizontal depth)
        var avgX = locationsOnMap.Average(loc => loc.X);
        var avgZ = locationsOnMap.Average(loc => loc.Z ?? 0);

        var screenCoords = _currentMapConfig.GameToScreen(avgX, avgZ);
        if (screenCoords == null) return;

        // 목표 줌 레벨
        var targetZoom = Math.Max(2.0, _zoomLevel);

        // 목표 위치 계산
        var viewerCenterX = MapViewerGrid.ActualWidth / 2;
        var viewerCenterY = MapViewerGrid.ActualHeight / 2;
        var targetTranslateX = viewerCenterX - screenCoords.Value.screenX * targetZoom;
        var targetTranslateY = viewerCenterY - screenCoords.Value.screenY * targetZoom;

        // 부드러운 애니메이션 적용
        AnimateMapTo(targetZoom, targetTranslateX, targetTranslateY);

        // 상태 텍스트 업데이트
        var questName = !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;
        StatusText.Text = $"포커스: {questName}";

        // 해당 마커 일시 강조
        var objectiveId = objective.ObjectiveId;
        if (_markersByObjectiveId.TryGetValue(objectiveId, out var markers))
        {
            foreach (var marker in markers)
            {
                StartPulseAnimation(marker);

                // 2초 후 펄스 중지
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                var capturedMarker = marker;
                timer.Tick += (s, e) =>
                {
                    StopPulseAnimation(capturedMarker);
                    timer.Stop();
                };
                timer.Start();
            }
        }
    }

    /// <summary>
    /// 맵을 목표 줌/위치로 부드럽게 애니메이션
    /// </summary>
    private void AnimateMapTo(double targetZoom, double targetTranslateX, double targetTranslateY)
    {
        var duration = TimeSpan.FromMilliseconds(350);
        var easing = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

        // 줌 애니메이션
        var zoomAnimationX = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = targetZoom,
            Duration = duration,
            EasingFunction = easing
        };
        var zoomAnimationY = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = targetZoom,
            Duration = duration,
            EasingFunction = easing
        };

        // 이동 애니메이션
        var translateAnimationX = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = targetTranslateX,
            Duration = duration,
            EasingFunction = easing
        };
        var translateAnimationY = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = targetTranslateY,
            Duration = duration,
            EasingFunction = easing
        };

        // 줌 텍스트 업데이트를 위한 이벤트
        zoomAnimationX.Completed += (s, e) =>
        {
            _zoomLevel = targetZoom;
            ZoomText.Text = $"{_zoomLevel * 100:F0}%";
            RedrawAll(); // 마커 스케일 업데이트
            UpdateMinimapViewport(); // 미니맵 뷰포트 업데이트
        };

        // 애니메이션 시작
        MapScale.BeginAnimation(ScaleTransform.ScaleXProperty, zoomAnimationX);
        MapScale.BeginAnimation(ScaleTransform.ScaleYProperty, zoomAnimationY);
        MapTranslate.BeginAnimation(TranslateTransform.XProperty, translateAnimationX);
        MapTranslate.BeginAnimation(TranslateTransform.YProperty, translateAnimationY);
    }

    #endregion
}
