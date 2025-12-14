using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using TarkovDBEditor.Models;
using TarkovDBEditor.Services;

namespace TarkovDBEditor.Views;

/// <summary>
/// Floor Setup Window - Y 좌표에 따른 자동 Floor 감지 설정
/// </summary>
public partial class FloorSetupWindow : Window
{
    private readonly FloorLocationService _floorLocationService = FloorLocationService.Instance;
    private readonly ScreenshotWatcherService _watcherService = ScreenshotWatcherService.Instance;

    private readonly ObservableCollection<MapFloorLocation> _floorLocations = new();
    private MapConfigList? _mapConfigList;
    private MapConfig? _currentMapConfig;
    private MapFloorConfig? _currentFloor;

    public FloorSetupWindow()
    {
        InitializeComponent();

        FloorLocationsGrid.ItemsSource = _floorLocations;

        Loaded += FloorSetupWindow_Loaded;
        Closed += FloorSetupWindow_Closed;
    }

    private async void FloorSetupWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 맵 설정 로드
        await LoadMapConfigsAsync();

        // 스크린샷 워처 이벤트 구독
        _watcherService.PositionDetected += OnPositionDetected;
        _watcherService.StateChanged += OnWatcherStateChanged;

        UpdateWatcherStatus();
    }

    private void FloorSetupWindow_Closed(object? sender, EventArgs e)
    {
        // 이벤트 구독 해제
        _watcherService.PositionDetected -= OnPositionDetected;
        _watcherService.StateChanged -= OnWatcherStateChanged;
    }

    private async Task LoadMapConfigsAsync()
    {
        try
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Data", "map_configs.json");
            if (File.Exists(configPath))
            {
                var json = await File.ReadAllTextAsync(configPath);
                _mapConfigList = JsonSerializer.Deserialize<MapConfigList>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                MapSelector.Items.Clear();
                if (_mapConfigList != null)
                {
                    foreach (var config in _mapConfigList.Maps.Where(m => m.Floors != null && m.Floors.Count > 0))
                    {
                        var item = new ComboBoxItem
                        {
                            Content = config.DisplayName,
                            Tag = config.Key
                        };
                        MapSelector.Items.Add(item);
                    }
                }

                if (MapSelector.Items.Count > 0)
                {
                    MapSelector.SelectedIndex = 0;
                }
            }
            else
            {
                MessageBox.Show("map_configs.json 파일을 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"맵 설정 로드 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void MapSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MapSelector.SelectedItem is not ComboBoxItem selectedItem)
            return;

        var mapKey = selectedItem.Tag as string;
        if (string.IsNullOrEmpty(mapKey) || _mapConfigList == null)
            return;

        _currentMapConfig = _mapConfigList.Maps.FirstOrDefault(m => m.Key == mapKey);
        if (_currentMapConfig == null)
            return;

        // Floor 콤보박스 업데이트
        FloorSelector.Items.Clear();
        if (_currentMapConfig.Floors != null)
        {
            foreach (var floor in _currentMapConfig.Floors.OrderBy(f => f.Order))
            {
                var item = new ComboBoxItem
                {
                    Content = floor.DisplayName,
                    Tag = floor.LayerId
                };
                FloorSelector.Items.Add(item);

                if (floor.IsDefault)
                {
                    FloorSelector.SelectedItem = item;
                }
            }
        }

        if (FloorSelector.SelectedItem == null && FloorSelector.Items.Count > 0)
        {
            FloorSelector.SelectedIndex = 0;
        }
    }

    private async void FloorSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FloorSelector.SelectedItem is not ComboBoxItem selectedItem)
            return;

        var floorId = selectedItem.Tag as string;
        if (string.IsNullOrEmpty(floorId) || _currentMapConfig == null)
            return;

        _currentFloor = _currentMapConfig.Floors?.FirstOrDefault(f => f.LayerId == floorId);

        // Floor Locations 로드
        await LoadFloorLocationsAsync();
    }

    private async Task LoadFloorLocationsAsync()
    {
        if (_currentMapConfig == null || _currentFloor == null)
            return;

        try
        {
            var allLocations = await _floorLocationService.LoadByMapAsync(_currentMapConfig.Key);
            var filteredLocations = allLocations.Where(l => l.FloorId == _currentFloor.LayerId).ToList();

            _floorLocations.Clear();
            foreach (var loc in filteredLocations.OrderByDescending(l => l.Priority))
            {
                _floorLocations.Add(loc);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Floor Locations 로드 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMapConfig == null || _currentFloor == null)
        {
            MessageBox.Show("먼저 맵과 층을 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newLocation = new MapFloorLocation
        {
            MapKey = _currentMapConfig.Key,
            FloorId = _currentFloor.LayerId,
            RegionName = $"New Region {_floorLocations.Count + 1}",
            MinY = -10,
            MaxY = 10,
            Priority = 0
        };

        try
        {
            await _floorLocationService.SaveAsync(newLocation);
            _floorLocations.Add(newLocation);
            FloorLocationsGrid.SelectedItem = newLocation;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (FloorLocationsGrid.SelectedItem is not MapFloorLocation selectedLocation)
        {
            MessageBox.Show("삭제할 항목을 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"'{selectedLocation.RegionName}'을(를) 삭제하시겠습니까?",
            "삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _floorLocationService.DeleteAsync(selectedLocation.Id);
            _floorLocations.Remove(selectedLocation);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"삭제 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        if (FloorLocationsGrid.SelectedItem is not MapFloorLocation selectedLocation)
        {
            MessageBox.Show("복제할 항목을 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newLocation = new MapFloorLocation
        {
            MapKey = selectedLocation.MapKey,
            FloorId = selectedLocation.FloorId,
            RegionName = $"{selectedLocation.RegionName} (Copy)",
            MinY = selectedLocation.MinY,
            MaxY = selectedLocation.MaxY,
            MinX = selectedLocation.MinX,
            MaxX = selectedLocation.MaxX,
            MinZ = selectedLocation.MinZ,
            MaxZ = selectedLocation.MaxZ,
            Priority = selectedLocation.Priority
        };

        try
        {
            await _floorLocationService.SaveAsync(newLocation);
            _floorLocations.Add(newLocation);
            FloorLocationsGrid.SelectedItem = newLocation;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"복제 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void EditOnMapButton_Click(object sender, RoutedEventArgs e)
    {
        if (FloorLocationsGrid.SelectedItem is not MapFloorLocation selectedLocation)
        {
            MessageBox.Show("지도에서 편집할 항목을 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_currentMapConfig == null || _currentFloor == null)
        {
            MessageBox.Show("맵과 층이 선택되어 있어야 합니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editor = new FloorRangeEditorWindow(
            selectedLocation,
            _currentMapConfig.Key,
            _currentFloor.LayerId,
            _currentFloor.DisplayName)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true && editor.WasSaved)
        {
            // Apply the results
            selectedLocation.MinX = editor.ResultMinX;
            selectedLocation.MaxX = editor.ResultMaxX;
            selectedLocation.MinZ = editor.ResultMinZ;
            selectedLocation.MaxZ = editor.ResultMaxZ;

            try
            {
                await _floorLocationService.SaveAsync(selectedLocation);

                // Refresh grid
                FloorLocationsGrid.Items.Refresh();

                MessageBox.Show(
                    $"범위가 적용되었습니다.\n\n" +
                    $"X: {selectedLocation.MinX:F1} ~ {selectedLocation.MaxX:F1}\n" +
                    $"Z: {selectedLocation.MinZ:F1} ~ {selectedLocation.MaxZ:F1}",
                    "적용 완료",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void TestPositionButton_Click(object sender, RoutedEventArgs e)
    {
        var position = _watcherService.CurrentPosition;
        if (position == null)
        {
            MessageBox.Show("현재 감지된 위치가 없습니다.\n스크린샷 워처가 실행 중인지 확인해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await TestPositionAsync(position);
    }

    private async Task TestPositionAsync(EftPosition position)
    {
        if (_currentMapConfig == null)
            return;

        // 현재 맵의 모든 Floor Location으로 테스트
        var detectedFloor = await _floorLocationService.DetectFloorAsync(
            _currentMapConfig.Key,
            position.X,
            position.Y,
            position.Z);

        CurrentPositionText.Text = $"Position: X:{position.X:F1}, Y:{position.Y:F1}, Z:{position.Z:F1}";
        DetectedFloorText.Text = detectedFloor ?? "(No match)";

        // 감지된 Floor가 있으면 해당 Floor로 전환
        if (!string.IsNullOrEmpty(detectedFloor))
        {
            var floorItem = FloorSelector.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag as string == detectedFloor);

            if (floorItem != null && FloorSelector.SelectedItem != floorItem)
            {
                FloorSelector.SelectedItem = floorItem;
            }
        }
    }

    private async void FloorLocationsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel)
            return;

        if (e.Row.Item is not MapFloorLocation location)
            return;

        // 변경사항 저장 (약간의 지연 후)
        await Task.Delay(100);

        try
        {
            await _floorLocationService.SaveAsync(location);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnPositionDetected(object? sender, PositionDetectedEventArgs e)
    {
        Dispatcher.Invoke(async () =>
        {
            await TestPositionAsync(e.Position);
        });
    }

    private void OnWatcherStateChanged(object? sender, WatcherStateChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateWatcherStatus();
        });
    }

    private void UpdateWatcherStatus()
    {
        if (_watcherService.IsWatching)
        {
            WatcherStatusText.Text = $"Watcher: Running ({_watcherService.CurrentWatchPath})";
            WatcherStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x70, 0xA8, 0x00));
        }
        else
        {
            WatcherStatusText.Text = "Watcher: Not running";
            WatcherStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80));
        }

        var position = _watcherService.CurrentPosition;
        if (position != null)
        {
            CurrentPositionText.Text = $"Position: X:{position.X:F1}, Y:{position.Y:F1}, Z:{position.Z:F1}";
        }
        else
        {
            CurrentPositionText.Text = "Position: --";
        }
    }
}
