using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TarkovDBEditor.Models;
using TarkovDBEditor.Services;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace TarkovDBEditor.Views;

/// <summary>
/// Map Transfer Window - Tarkov Market API 마커를 DB로 가져오는 도구
/// </summary>
public partial class MapTransferWindow : Window
{
    // Zoom settings
    private double _zoomLevel = 1.0;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 12.0;
    private static readonly double[] ZoomPresets = { 0.1, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0, 12.0 };

    // Drag state
    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartTranslateX;
    private double _dragStartTranslateY;

    // Map configuration
    private MapConfigList? _mapConfigs;
    private MapConfig? _currentMapConfig;

    // Floor management
    private string? _currentFloorId;
    private List<MapFloorConfig>? _sortedFloors;

    // Data
    private readonly List<MapMarker> _dbMarkers = new();
    private readonly List<TarkovMarketMarker> _apiMarkers = new();
    private readonly List<MarkerMatchResult> _matchResults = new();
    private readonly HashSet<string> _selectedApiMarkers = new();

    // Transform
    private double[]? _currentTransform;
    private double _currentError;

    // TPS Transform (비선형 보간)
    private ThinPlateSplineTransform? _currentTps;
    private bool _useTps = true; // TPS 사용 여부 (Fallback 제어)

    // Services
    private readonly TarkovMarketService _marketService;

    // Icon cache
    private static readonly Dictionary<MapMarkerType, BitmapImage?> _iconCache = new();

    // Category filter
    private string? _categoryFilter;

    // DB 마커 드래그 (Ctrl+클릭)
    private bool _isDraggingDbMarker;
    private MapMarker? _draggingDbMarker;
    private Point _dbMarkerDragStart;

    // DB 마커 임시 수정 위치 (마커 ID -> 수정된 게임 좌표)
    private readonly Dictionary<string, (double X, double Z)> _modifiedDbPositions = new();

    public MapTransferWindow()
    {
        InitializeComponent();
        _marketService = new TarkovMarketService();
        LoadMapConfigs();
        Loaded += MapTransferWindow_Loaded;
        PreviewKeyDown += MapTransferWindow_KeyDown;
        Closed += MapTransferWindow_Closed;
    }

    private void MapTransferWindow_Closed(object? sender, EventArgs e)
    {
        _marketService.Dispose();
    }

    private async void MapTransferWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDbMarkersAsync();

        if (MapSelector.Items.Count > 0)
        {
            MapSelector.SelectedIndex = 0;
        }
    }

    private void LoadMapConfigs()
    {
        try
        {
            var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Data", "map_configs.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                _mapConfigs = JsonSerializer.Deserialize<MapConfigList>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (_mapConfigs != null)
                {
                    foreach (var map in _mapConfigs.Maps)
                    {
                        MapSelector.Items.Add(map);
                    }
                }
            }
            else
            {
                StatusText.Text = "Warning: map_configs.json not found";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading map configs: {ex.Message}";
        }
    }

    private async Task LoadDbMarkersAsync()
    {
        _dbMarkers.Clear();

        try
        {
            var markers = await MapMarkerService.Instance.LoadAllMarkersAsync();
            foreach (var marker in markers)
            {
                _dbMarkers.Add(marker);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading DB markers: {ex.Message}";
        }
    }

    #region Map Selection & Floor Management

    private void MapSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MapSelector.SelectedItem is MapConfig config)
        {
            _currentMapConfig = config;
            UpdateFloorSelector(config);
            LoadMap(config);

            // Reset state
            _apiMarkers.Clear();
            _matchResults.Clear();
            _selectedApiMarkers.Clear();
            _currentTransform = null;
            _currentTps = null;
            _currentError = 0;

            UpdateCounts();
            UpdateButtonStates();
            UpdateTransformInfo();
            RedrawAll();
        }
    }

    private void UpdateFloorSelector(MapConfig config)
    {
        FloorSelector.Items.Clear();
        _currentFloorId = null;
        _sortedFloors = null;

        var floors = config.Floors;
        if (floors == null || floors.Count == 0)
        {
            TxtFloorLabel.Visibility = Visibility.Collapsed;
            FloorSelector.Visibility = Visibility.Collapsed;
            return;
        }

        TxtFloorLabel.Visibility = Visibility.Visible;
        FloorSelector.Visibility = Visibility.Visible;

        _sortedFloors = floors.OrderBy(f => f.Order).ToList();

        int defaultIndex = 0;
        for (int i = 0; i < _sortedFloors.Count; i++)
        {
            var floor = _sortedFloors[i];
            FloorSelector.Items.Add(new ComboBoxItem
            {
                Content = floor.DisplayName,
                Tag = floor.LayerId
            });

            if (floor.IsDefault)
            {
                defaultIndex = i;
            }
        }

        if (FloorSelector.Items.Count > 0)
        {
            FloorSelector.SelectedIndex = defaultIndex;
            _currentFloorId = _sortedFloors[defaultIndex].LayerId;
        }
    }

    private void FloorSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FloorSelector.SelectedItem is ComboBoxItem floorItem && floorItem.Tag is string floorId)
        {
            if (_currentFloorId != floorId)
            {
                _currentFloorId = floorId;

                if (_currentMapConfig != null)
                {
                    LoadMap(_currentMapConfig, resetView: false);
                    RedrawAll();
                }
            }
        }
    }

    private void MapTransferWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (_sortedFloors == null || _sortedFloors.Count == 0)
            return;

        int floorIndex = -1;

        switch (e.Key)
        {
            case Key.NumPad0: floorIndex = 0; break;
            case Key.NumPad1: floorIndex = 1; break;
            case Key.NumPad2: floorIndex = 2; break;
            case Key.NumPad3: floorIndex = 3; break;
            case Key.NumPad4: floorIndex = 4; break;
            case Key.NumPad5: floorIndex = 5; break;
        }

        if (floorIndex >= 0 && floorIndex < _sortedFloors.Count)
        {
            FloorSelector.SelectedIndex = floorIndex;
            e.Handled = true;
        }
    }

    #endregion

    #region Map Loading

    private void LoadMap(MapConfig config, bool resetView = true)
    {
        try
        {
            var svgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Maps", config.SvgFileName);

            if (!File.Exists(svgPath))
            {
                MessageBox.Show($"Map file not found: {svgPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Floor filtering
            IEnumerable<string>? visibleFloors = null;
            IEnumerable<string>? allFloors = null;
            string? backgroundFloorId = null;
            double backgroundOpacity = 0.3;

            if (config.Floors != null && config.Floors.Count > 0 && !string.IsNullOrEmpty(_currentFloorId))
            {
                allFloors = config.Floors.Select(f => f.LayerId);
                visibleFloors = new[] { _currentFloorId };

                var defaultFloor = config.Floors.FirstOrDefault(f => f.IsDefault);
                var currentFloor = config.Floors.FirstOrDefault(f =>
                    string.Equals(f.LayerId, _currentFloorId, StringComparison.OrdinalIgnoreCase));

                if (defaultFloor != null && !string.Equals(_currentFloorId, defaultFloor.LayerId, StringComparison.OrdinalIgnoreCase))
                {
                    backgroundFloorId = defaultFloor.LayerId;

                    if (currentFloor != null && currentFloor.Order < 0)
                    {
                        backgroundOpacity = 0.15;
                    }
                }
            }

            if (visibleFloors != null)
            {
                var preprocessor = new SvgStylePreprocessor();
                var processedSvg = preprocessor.ProcessSvgFile(svgPath, visibleFloors, allFloors, backgroundFloorId, backgroundOpacity);

                var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"map_transfer_{Guid.NewGuid()}.svg");
                File.WriteAllText(tempPath, processedSvg);
                MapSvg.Source = new Uri(tempPath, UriKind.Absolute);
            }
            else
            {
                MapSvg.Source = new Uri(svgPath, UriKind.Absolute);
            }

            MapSvg.Width = config.ImageWidth;
            MapSvg.Height = config.ImageHeight;

            MapCanvas.Width = config.ImageWidth;
            MapCanvas.Height = config.ImageHeight;
            Canvas.SetLeft(MapSvg, 0);
            Canvas.SetTop(MapSvg, 0);

            if (resetView)
            {
                SetZoom(1.0);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    CenterMapInView();
                    RedrawAll();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                RedrawAll();
            }

            var floorInfo = !string.IsNullOrEmpty(_currentFloorId) ? $" [{_currentFloorId}]" : "";
            StatusText.Text = $"Loaded: {config.DisplayName}{floorInfo}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load map: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region API Operations

    private async void BtnFetchApi_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMapConfig == null) return;

        BtnFetchApi.IsEnabled = false;
        StatusText.Text = $"Fetching markers and quests from Tarkov Market API...";

        try
        {
            _apiMarkers.Clear();
            _matchResults.Clear();
            _selectedApiMarkers.Clear();

            // 마커와 퀘스트 병렬 로드
            var markersTask = _marketService.FetchMarkersAsync(_currentMapConfig.Key, useCache: false);
            var questsTask = _marketService.FetchQuestsAsync(useCache: true);

            await Task.WhenAll(markersTask, questsTask);

            var markers = await markersTask;
            var quests = await questsTask;

            // Geometry가 있는 마커만 필터링
            var validMarkers = markers.Where(m => m.Geometry != null).ToList();
            _apiMarkers.AddRange(validMarkers);

            var skippedCount = markers.Count - validMarkers.Count;
            var statusMsg = $"Fetched {validMarkers.Count} markers, {quests.Count} quests";
            if (skippedCount > 0)
            {
                statusMsg += $" (skipped {skippedCount} without geometry)";
            }
            StatusText.Text = statusMsg;

            // 카테고리별 통계 표시
            var categories = validMarkers.GroupBy(m => m.Category)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();
            if (categories.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[FetchApi] Categories: {string.Join(", ", categories)}");
            }

            // 퀘스트 연결된 마커 수 표시
            var questLinkedCount = validMarkers.Count(m => !string.IsNullOrEmpty(m.QuestUid));
            System.Diagnostics.Debug.WriteLine($"[FetchApi] Markers with questUid: {questLinkedCount}");

            UpdateCounts();
            UpdateButtonStates();
            RedrawAll();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error fetching API: {ex.Message}";
            MessageBox.Show($"Failed to fetch API data:\n\n{ex.Message}\n\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnFetchApi.IsEnabled = true;
        }
    }

    private void BtnAutoMatch_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMapConfig == null) return;

        var dbMarkersForMap = _dbMarkers.Where(m => m.MapKey == _currentMapConfig.Key).ToList();

        _matchResults.Clear();
        var matches = MarkerMatchingService.AutoMatch(dbMarkersForMap, _apiMarkers);
        _matchResults.AddRange(matches);

        // 모든 매칭을 참조점으로 설정
        foreach (var match in _matchResults)
        {
            match.IsReferencePoint = true;
        }

        // 3개 이상 매칭되면 자동으로 Transform 계산 및 적용
        if (_matchResults.Count >= 3)
        {
            CalculateAndApplyTransform();
            StatusText.Text = $"Auto-matched {_matchResults.Count} markers, transform applied (error: {_currentError:F2})";
        }
        else
        {
            StatusText.Text = $"Auto-matched {_matchResults.Count} markers (need 3+ for transform)";
        }

        UpdateCounts();
        UpdateButtonStates();
        UpdateTransformInfo();
        RedrawAll();
    }

    /// <summary>
    /// 매칭된 참조점으로 Transform 계산 및 모든 API 마커에 적용
    /// TPS (Thin Plate Spline) 비선형 보간 사용, 실패 시 Delaunay/Affine 폴백
    /// </summary>
    private void CalculateAndApplyTransform()
    {
        if (_currentMapConfig == null) return;

        // 참조점 수집 (SVG 좌표 → DB 좌표 매핑)
        var referencePoints = _matchResults
            .Where(m => m.IsReferencePoint && m.ApiMarker.Geometry != null)
            .Select(m =>
            {
                // 수정된 위치가 있으면 사용, 없으면 원본 사용
                double dbX = m.DbMarker.X;
                double dbZ = m.DbMarker.Z;

                if (_modifiedDbPositions.TryGetValue(m.DbMarker.Id, out var modifiedPos))
                {
                    dbX = modifiedPos.X;
                    dbZ = modifiedPos.Z;
                }

                return (dbX, dbZ, svgX: m.ApiMarker.Geometry!.X, svgY: m.ApiMarker.Geometry!.Y);
            })
            .ToList();

        if (referencePoints.Count < 3) return;

        // === TPS (Thin Plate Spline) 변환 시도 ===
        _currentTps = null;
        bool usedTps = false;

        if (_useTps)
        {
            _currentTps = TpsTransformFactory.Create(referencePoints, lambda: 0.0);

            if (_currentTps != null)
            {
                usedTps = true;
                _currentError = _currentTps.MeanError;
                System.Diagnostics.Debug.WriteLine($"[TPS] Successfully computed: {referencePoints.Count} points, mean error={_currentError:F4}, max error={_currentTps.MaxError:F4}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[TPS] Computation failed, falling back to Affine + Delaunay");
            }
        }

        // === Fallback: Affine Transform + Delaunay 삼각분할 ===
        List<CoordinateTransformService.Triangle>? triangles = null;
        List<(double svgX, double svgY, double dbX, double dbZ)>? interpolationPoints = null;

        if (!usedTps)
        {
            // Affine Transform 계산
            _currentTransform = CoordinateTransformService.CalculateAffineTransform(referencePoints);

            if (_currentTransform == null) return;

            _currentError = CoordinateTransformService.CalculateError(referencePoints, _currentTransform);

            // Delaunay 삼각분할용 포인트 리스트 생성
            interpolationPoints = referencePoints
                .Select(p => (svgX: p.svgX, svgY: p.svgY, dbX: p.dbX, dbZ: p.dbZ))
                .ToList();

            // Delaunay 삼각분할 생성
            triangles = CoordinateTransformService.CreateDelaunayTriangulation(interpolationPoints);

            System.Diagnostics.Debug.WriteLine($"[Affine+Delaunay] Created {triangles.Count} triangles from {interpolationPoints.Count} reference points, mean error={_currentError:F4}");
        }

        // === 모든 API 마커에 변환 적용 ===
        foreach (var marker in _apiMarkers)
        {
            if (marker.Geometry == null) continue;

            var svgX = marker.Geometry.X;
            var svgY = marker.Geometry.Y;

            // 매칭된 마커인지 확인
            var match = _matchResults.FirstOrDefault(m => m.ApiMarker.Uid == marker.Uid);

            if (match != null)
            {
                // 매칭된 마커: DB 좌표로 정확히 스냅 (오차 = 0)
                if (_modifiedDbPositions.TryGetValue(match.DbMarker.Id, out var modPos))
                {
                    marker.GameX = modPos.X;
                    marker.GameZ = modPos.Z;
                }
                else
                {
                    marker.GameX = match.DbMarker.X;
                    marker.GameZ = match.DbMarker.Z;
                }
                marker.FloorId = match.DbMarker.FloorId;
                match.DistanceError = 0;
            }
            else
            {
                // 비매칭 마커: 변환 적용
                if (usedTps && _currentTps != null)
                {
                    // TPS 변환 사용
                    var (gameX, gameZ) = _currentTps.Transform(svgX, svgY);
                    marker.GameX = gameX;
                    marker.GameZ = gameZ;
                }
                else if (triangles != null && triangles.Count > 0 && interpolationPoints != null)
                {
                    // Delaunay + Barycentric 보간 사용
                    var (gameX, gameZ) = CoordinateTransformService.InterpolatePoint(
                        svgX, svgY, triangles, interpolationPoints);
                    marker.GameX = gameX;
                    marker.GameZ = gameZ;
                }
                else if (_currentTransform != null)
                {
                    // Affine Transform 폴백
                    var (gameX, gameZ) = CoordinateTransformService.TransformSvgToGame(
                        svgX, svgY, _currentTransform);
                    marker.GameX = gameX;
                    marker.GameZ = gameZ;
                }

                marker.FloorId = MarkerMatchingService.MapLevelToFloorId(
                    marker.Level, _currentMapConfig.Key, _currentMapConfig.Floors);
            }
        }

        // 매칭된 마커들의 오차 계산 (정보 표시용)
        foreach (var match in _matchResults)
        {
            if (match.ApiMarker.Geometry == null) continue;

            double targetX = match.DbMarker.X;
            double targetZ = match.DbMarker.Z;
            if (_modifiedDbPositions.TryGetValue(match.DbMarker.Id, out var modPos))
            {
                targetX = modPos.X;
                targetZ = modPos.Z;
            }

            // 변환 방식에 따른 오차 계산 (디버그용)
            double calcX, calcZ;
            if (usedTps && _currentTps != null)
            {
                (calcX, calcZ) = _currentTps.Transform(match.ApiMarker.Geometry.X, match.ApiMarker.Geometry.Y);
            }
            else if (_currentTransform != null)
            {
                (calcX, calcZ) = CoordinateTransformService.TransformSvgToGame(
                    match.ApiMarker.Geometry.X, match.ApiMarker.Geometry.Y, _currentTransform);
            }
            else
            {
                continue;
            }

            var dx = calcX - targetX;
            var dz = calcZ - targetZ;
            var transformError = Math.Sqrt(dx * dx + dz * dz);

            System.Diagnostics.Debug.WriteLine($"[Transform] {match.DbMarker.Name}: {(usedTps ? "TPS" : "Affine")} error={transformError:F2}, Applied error=0 (snapped)");
        }
    }

    private void BtnCalcTransform_Click(object sender, RoutedEventArgs e)
    {
        var refCount = _matchResults.Count(m => m.IsReferencePoint && m.ApiMarker.Geometry != null);

        if (refCount < 3)
        {
            MessageBox.Show("At least 3 reference points are required to calculate transform.",
                "Insufficient Points", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CalculateAndApplyTransform();

        if (_currentTransform == null)
        {
            MessageBox.Show("Failed to calculate transform. Points may be collinear.",
                "Calculation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        StatusText.Text = $"Transform recalculated. Avg error: {_currentError:F2} units";
        UpdateTransformInfo();
        UpdateButtonStates();
        RedrawAll();
    }

    private void BtnApplyTransform_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTransform == null || _currentMapConfig == null) return;

        // API 마커들에 변환 적용 (이미 CalculateAndApplyTransform에서 처리됨)
        // 이 버튼은 수동으로 다시 적용할 때 사용
        foreach (var marker in _apiMarkers)
        {
            if (marker.Geometry == null) continue;

            var (gameX, gameZ) = CoordinateTransformService.TransformSvgToGame(
                marker.Geometry.X, marker.Geometry.Y, _currentTransform);

            marker.GameX = gameX;
            marker.GameZ = gameZ;
            marker.FloorId = MarkerMatchingService.MapLevelToFloorId(
                marker.Level, _currentMapConfig.Key, _currentMapConfig.Floors);
        }

        StatusText.Text = "변환이 모든 API 마커에 다시 적용되었습니다";
        RedrawAll();
    }

    private void BtnResetDbPositions_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMapConfig == null) return;

        // 현재 맵의 수정된 DB 마커 위치만 초기화
        var keysToRemove = _modifiedDbPositions.Keys
            .Where(key => _dbMarkers.Any(m => m.Id == key && m.MapKey == _currentMapConfig.Key))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _modifiedDbPositions.Remove(key);
        }

        StatusText.Text = $"DB 마커 위치가 원본으로 초기화되었습니다 ({keysToRemove.Count}개)";
        UpdateButtonStates();
        RedrawAll();
    }

    private void OffsetChanged(object sender, TextChangedEventArgs e)
    {
        // 입력 유효성 검사만 (실시간 적용 안함)
    }

    private void BtnApplyOffset_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMapConfig == null) return;

        // 오프셋 파싱
        if (!double.TryParse(TxtOffsetX.Text, out var offsetX))
        {
            offsetX = 0;
            TxtOffsetX.Text = "0";
        }

        if (!double.TryParse(TxtOffsetZ.Text, out var offsetZ))
        {
            offsetZ = 0;
            TxtOffsetZ.Text = "0";
        }

        if (Math.Abs(offsetX) < 0.001 && Math.Abs(offsetZ) < 0.001)
        {
            StatusText.Text = "오프셋이 0입니다. 변경 없음";
            return;
        }

        // 모든 API 마커에 오프셋 적용
        int appliedCount = 0;
        foreach (var marker in _apiMarkers)
        {
            if (marker.GameX.HasValue && marker.GameZ.HasValue)
            {
                marker.GameX += offsetX;
                marker.GameZ += offsetZ;
                appliedCount++;
            }
        }

        // 오프셋 필드 초기화
        TxtOffsetX.Text = "0";
        TxtOffsetZ.Text = "0";

        StatusText.Text = $"오프셋 (X:{offsetX:+0.#;-0.#}, Z:{offsetZ:+0.#;-0.#})이 {appliedCount}개 마커에 적용되었습니다";
        RedrawAll();
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        var filteredMarkers = GetFilteredApiMarkers();

        if (_selectedApiMarkers.Count == filteredMarkers.Count)
        {
            // 모두 선택되어 있으면 해제
            _selectedApiMarkers.Clear();
        }
        else
        {
            // 아니면 모두 선택
            _selectedApiMarkers.Clear();
            foreach (var marker in filteredMarkers)
            {
                _selectedApiMarkers.Add(marker.Uid);
            }
        }

        UpdateCounts();
        RedrawAll();
    }

    private async void BtnImportSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMapConfig == null || _selectedApiMarkers.Count == 0) return;

        // 좌표가 있는 마커만 필터링 (MarkerType 제한 없음 - ApiMarkers는 참조용)
        var markersToImport = _apiMarkers
            .Where(m => _selectedApiMarkers.Contains(m.Uid) &&
                       m.GameX.HasValue && m.GameZ.HasValue)
            .ToList();

        if (markersToImport.Count == 0)
        {
            MessageBox.Show("No valid markers to import. Make sure transform is applied.",
                "Nothing to Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Import {markersToImport.Count} markers to ApiMarkers table (reference data)?\n\nThese markers will be used as reference in Quest Validator and Map Preview.",
            "Confirm Import",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            // ApiMarker 목록 생성
            var apiMarkersToSave = markersToImport.Select(m =>
            {
                // 퀘스트 정보 가져오기
                var quest = _marketService.FindQuestByUid(m.QuestUid);

                return new ApiMarker
                {
                    Id = Guid.NewGuid().ToString(),
                    TarkovMarketUid = m.Uid,
                    Name = m.Name,
                    NameKo = m.NameL10n?.GetValueOrDefault("ko"),
                    Category = m.Category,
                    SubCategory = m.SubCategory,
                    MapKey = _currentMapConfig.Key,
                    X = m.GameX!.Value,
                    Y = 0,
                    Z = m.GameZ!.Value,
                    FloorId = m.FloorId,
                    QuestBsgId = quest?.BsgId,
                    QuestNameEn = quest?.NameEn,
                    ObjectiveDescription = m.Name, // 마커명을 objective description으로 사용
                    ImportedAt = DateTime.UtcNow
                };
            }).ToList();

            // ApiMarkerService로 저장
            await ApiMarkerService.Instance.SaveMarkersAsync(apiMarkersToSave);

            _selectedApiMarkers.Clear();
            UpdateCounts();
            RedrawAll();

            StatusText.Text = $"Saved {apiMarkersToSave.Count} markers to ApiMarkers table";
            MessageBox.Show($"Successfully saved {apiMarkersToSave.Count} reference markers.\n\nThese can be viewed in Quest Validator and Map Preview.",
                "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error saving markers: {ex.Message}";
            MessageBox.Show($"Failed to save markers:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Category Filter

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryFilter.SelectedItem is ComboBoxItem item)
        {
            var category = item.Content?.ToString();
            _categoryFilter = category == "All" ? null : category;
            RedrawAll();
        }
    }

    private List<TarkovMarketMarker> GetFilteredApiMarkers()
    {
        if (string.IsNullOrEmpty(_categoryFilter))
        {
            return _apiMarkers;
        }

        return _apiMarkers.Where(m =>
            string.Equals(m.Category, _categoryFilter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    #endregion

    #region UI Updates

    private void UpdateCounts()
    {
        if (_currentMapConfig == null)
        {
            DbMarkerCountText.Text = "0";
            ApiMarkerCountText.Text = "0";
            MatchedCountText.Text = "0";
            SelectedCountText.Text = "0";
            return;
        }

        var dbCount = _dbMarkers.Count(m => m.MapKey == _currentMapConfig.Key);
        var apiCount = GetFilteredApiMarkers().Count;
        var matchedCount = _matchResults.Count;
        var selectedCount = _selectedApiMarkers.Count;

        DbMarkerCountText.Text = dbCount.ToString();
        ApiMarkerCountText.Text = apiCount.ToString();
        MatchedCountText.Text = matchedCount.ToString();
        SelectedCountText.Text = selectedCount.ToString();
    }

    private void UpdateButtonStates()
    {
        BtnAutoMatch.IsEnabled = _apiMarkers.Count > 0;
        BtnCalcTransform.IsEnabled = _matchResults.Count(m => m.IsReferencePoint) >= 3;
        BtnApplyTransform.IsEnabled = _currentTransform != null || _currentTps != null;
        BtnSelectAll.IsEnabled = _apiMarkers.Count > 0;

        // Import 버튼: 선택된 마커가 있으면 활성화 (Transform 여부와 무관)
        BtnImportSelected.IsEnabled = _selectedApiMarkers.Count > 0;

        // 수정된 DB 마커가 있으면 초기화 버튼 활성화
        var hasModifiedDbMarkers = _currentMapConfig != null &&
            _modifiedDbPositions.Any(kvp =>
                _dbMarkers.Any(m => m.Id == kvp.Key && m.MapKey == _currentMapConfig.Key));
        BtnResetDbPositions.IsEnabled = hasModifiedDbMarkers;
    }

    private void UpdateTransformInfo()
    {
        var refCount = _matchResults.Count(m => m.IsReferencePoint);
        RefPointCountText.Text = refCount.ToString();

        // TPS 정보 표시 (우선)
        if (_currentTps != null && _currentTps.IsComputed)
        {
            TransformInfoText.Text = $"TPS ({_currentTps.ReferencePointCount} pts, λ={_currentTps.Lambda:E1})";
            ErrorText.Text = $"Mean: {_currentTps.MeanError:F2}, Max: {_currentTps.MaxError:F2}";
        }
        else if (_currentTransform != null)
        {
            // Affine 폴백 정보
            TransformInfoText.Text = $"Affine [{_currentTransform[0]:F4}, {_currentTransform[1]:F4}, {_currentTransform[2]:F4}, {_currentTransform[3]:F4}]";
            ErrorText.Text = $"{_currentError:F2} units";
        }
        else
        {
            TransformInfoText.Text = "Not calculated";
            ErrorText.Text = "-";
        }
    }

    private void UpdateModifiedCount()
    {
        if (_currentMapConfig == null) return;

        var modifiedCount = _modifiedDbPositions.Count(kvp =>
            _dbMarkers.Any(m => m.Id == kvp.Key && m.MapKey == _currentMapConfig.Key));

        // 수정된 마커 개수를 상태바에 표시
        if (modifiedCount > 0)
        {
            StatusText.Text = $"수정된 DB 마커: {modifiedCount}개 (Ctrl+클릭으로 드래그, 재계산 버튼으로 Transform 업데이트)";
        }
    }

    private void LayerVisibility_Changed(object sender, RoutedEventArgs e)
    {
        RedrawAll();
    }

    #endregion

    #region Icon Loading

    private BitmapImage? GetMarkerIcon(MapMarkerType markerType)
    {
        if (_iconCache.TryGetValue(markerType, out var cachedIcon))
        {
            return cachedIcon;
        }

        try
        {
            var iconFileName = MapMarker.GetIconFileName(markerType);
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Icons", iconFileName);

            if (File.Exists(iconPath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                bitmap.DecodePixelWidth = 64;
                bitmap.EndInit();
                bitmap.Freeze();
                _iconCache[markerType] = bitmap;
                return bitmap;
            }
        }
        catch
        {
            // Ignore icon loading errors
        }

        _iconCache[markerType] = null;
        return null;
    }

    #endregion

    #region Drawing

    private void RedrawAll()
    {
        if (!IsLoaded) return;

        DbMarkersCanvas?.Children.Clear();
        ApiMarkersCanvas?.Children.Clear();
        MatchLinesCanvas?.Children.Clear();

        if (ChkShowMatched?.IsChecked == true)
        {
            DrawMatchLines();
        }

        if (ChkShowDbMarkers?.IsChecked == true)
        {
            DrawDbMarkers();
        }

        if (ChkShowApiMarkers?.IsChecked == true)
        {
            DrawApiMarkers();
        }
    }

    private void DrawDbMarkers()
    {
        if (DbMarkersCanvas == null || _currentMapConfig == null) return;

        var inverseScale = 1.0 / _zoomLevel;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        var markersForMap = _dbMarkers.Where(m => m.MapKey == _currentMapConfig.Key).ToList();

        foreach (var marker in markersForMap)
        {
            // 수정된 위치가 있으면 사용, 없으면 원본 사용
            double gameX = marker.X;
            double gameZ = marker.Z;
            bool isModified = false;

            if (_modifiedDbPositions.TryGetValue(marker.Id, out var modifiedPos))
            {
                gameX = modifiedPos.X;
                gameZ = modifiedPos.Z;
                isModified = true;
            }

            var (sx, sy) = _currentMapConfig.GameToScreen(gameX, gameZ);

            double opacity = 1.0;
            if (hasFloors && _currentFloorId != null && marker.FloorId != null)
            {
                opacity = string.Equals(marker.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.3;
            }

            var (r, g, b) = MapMarker.GetMarkerColor(marker.MarkerType);
            var markerColor = Color.FromArgb((byte)(opacity * 255), r, g, b);

            var markerSize = 48 * inverseScale;
            var iconImage = GetMarkerIcon(marker.MarkerType);

            // DB 마커 구분용 원형 테두리 (수정된 경우 주황색, 아닌 경우 녹색)
            var outlineColor = isModified
                ? Color.FromArgb((byte)(opacity * 255), 255, 152, 0)   // Orange #FF9800 (수정됨)
                : Color.FromArgb((byte)(opacity * 255), 76, 175, 80);  // Green #4CAF50 (원본)

            var dbOutline = new Ellipse
            {
                Width = markerSize + 12 * inverseScale,
                Height = markerSize + 12 * inverseScale,
                Fill = Brushes.Transparent,
                Stroke = new SolidColorBrush(outlineColor),
                StrokeThickness = (isModified ? 4 : 3) * inverseScale,
                Tag = marker.Id,  // 클릭 감지용
                Cursor = Cursors.Hand
            };
            Canvas.SetLeft(dbOutline, sx - (markerSize + 12 * inverseScale) / 2);
            Canvas.SetTop(dbOutline, sy - (markerSize + 12 * inverseScale) / 2);
            DbMarkersCanvas.Children.Add(dbOutline);

            if (iconImage != null)
            {
                var image = new Image
                {
                    Source = iconImage,
                    Width = markerSize,
                    Height = markerSize,
                    Opacity = opacity
                };

                Canvas.SetLeft(image, sx - markerSize / 2);
                Canvas.SetTop(image, sy - markerSize / 2);
                DbMarkersCanvas.Children.Add(image);
            }
            else
            {
                // Fallback to colored circle
                var circle = new Ellipse
                {
                    Width = markerSize,
                    Height = markerSize,
                    Fill = new SolidColorBrush(markerColor),
                    Stroke = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                    StrokeThickness = 3 * inverseScale
                };

                Canvas.SetLeft(circle, sx - markerSize / 2);
                Canvas.SetTop(circle, sy - markerSize / 2);
                DbMarkersCanvas.Children.Add(circle);

                // Icon text
                var iconText = marker.MarkerType switch
                {
                    MapMarkerType.PmcSpawn => "P",
                    MapMarkerType.ScavSpawn => "S",
                    MapMarkerType.PmcExtraction => "E",
                    MapMarkerType.ScavExtraction => "E",
                    MapMarkerType.SharedExtraction => "E",
                    MapMarkerType.Transit => "T",
                    MapMarkerType.BossSpawn => "B",
                    MapMarkerType.RaiderSpawn => "R",
                    MapMarkerType.Lever => "L",
                    MapMarkerType.Keys => "K",
                    _ => "?"
                };

                var icon = new TextBlock
                {
                    Text = iconText,
                    Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                    FontSize = 24 * inverseScale,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center
                };

                icon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(icon, sx - icon.DesiredSize.Width / 2);
                Canvas.SetTop(icon, sy - icon.DesiredSize.Height / 2);
                DbMarkersCanvas.Children.Add(icon);
            }

            // "DB" 또는 "수정됨" 배지 (마커 우측 상단)
            var badgeColor = isModified
                ? Color.FromArgb((byte)(opacity * 230), 255, 152, 0)   // Orange
                : Color.FromArgb((byte)(opacity * 230), 76, 175, 80);  // Green

            var dbBadge = new Border
            {
                Background = new SolidColorBrush(badgeColor),
                CornerRadius = new CornerRadius(3 * inverseScale),
                Padding = new Thickness(3 * inverseScale, 1 * inverseScale, 3 * inverseScale, 1 * inverseScale),
                Child = new TextBlock
                {
                    Text = isModified ? "수정" : "DB",
                    Foreground = Brushes.White,
                    FontSize = 12 * inverseScale,
                    FontWeight = FontWeights.Bold
                }
            };
            dbBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(dbBadge, sx + markerSize / 2 - 4 * inverseScale);
            Canvas.SetTop(dbBadge, sy - markerSize / 2 - 8 * inverseScale);
            DbMarkersCanvas.Children.Add(dbBadge);

            // Name label
            var nameLabel = new TextBlock
            {
                Text = marker.Name,
                Foreground = new SolidColorBrush(markerColor),
                FontSize = 24 * inverseScale,
                FontWeight = FontWeights.SemiBold
            };

            Canvas.SetLeft(nameLabel, sx + markerSize / 2 + 8 * inverseScale);
            Canvas.SetTop(nameLabel, sy - 12 * inverseScale);
            DbMarkersCanvas.Children.Add(nameLabel);

            // Floor label (if different floor)
            if (hasFloors && marker.FloorId != null && opacity < 1.0)
            {
                var floorDisplayName = _sortedFloors?
                    .FirstOrDefault(f => string.Equals(f.LayerId, marker.FloorId, StringComparison.OrdinalIgnoreCase))
                    ?.DisplayName ?? marker.FloorId;

                var floorLabel = new TextBlock
                {
                    Text = $"[{floorDisplayName}]",
                    Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 200), 154, 136, 102)),
                    FontSize = 18 * inverseScale,
                    FontStyle = FontStyles.Italic
                };

                Canvas.SetLeft(floorLabel, sx + markerSize / 2 + 8 * inverseScale);
                Canvas.SetTop(floorLabel, sy + 14 * inverseScale);
                DbMarkersCanvas.Children.Add(floorLabel);
            }
        }
    }

    private void DrawApiMarkers()
    {
        if (ApiMarkersCanvas == null || _currentMapConfig == null) return;

        var inverseScale = 1.0 / _zoomLevel;
        var filteredMarkers = GetFilteredApiMarkers();
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        foreach (var marker in filteredMarkers)
        {
            // Geometry가 없는 마커는 스킵
            if (marker.Geometry == null) continue;

            double sx, sy;

            // 변환이 적용되었으면 변환된 게임 좌표 사용
            if (marker.GameX.HasValue && marker.GameZ.HasValue)
            {
                (sx, sy) = _currentMapConfig.GameToScreen(marker.GameX.Value, marker.GameZ.Value);
            }
            else
            {
                // 변환 전이면 SVG 좌표를 임시로 화면에 표시 (스케일 조정)
                // 이 경우 대략적인 위치만 표시
                sx = marker.Geometry.X * 10 + _currentMapConfig.ImageWidth / 2;
                sy = marker.Geometry.Y * 10 + _currentMapConfig.ImageHeight / 2;
            }

            // Floor opacity 계산
            double opacity = 1.0;
            if (hasFloors && _currentFloorId != null && marker.FloorId != null)
            {
                opacity = string.Equals(marker.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.3;
            }

            var isSelected = _selectedApiMarkers.Contains(marker.Uid);
            var markerSize = (isSelected ? 52 : 44) * inverseScale;

            // API 마커 구분용 파란색 다이아몬드 테두리 (모든 API 마커에 적용)
            var diamondSize = markerSize + 16 * inverseScale;
            var apiOutline = new Polygon
            {
                Points = new PointCollection
                {
                    new Point(sx, sy - diamondSize / 2),              // 상단
                    new Point(sx + diamondSize / 2, sy),              // 우측
                    new Point(sx, sy + diamondSize / 2),              // 하단
                    new Point(sx - diamondSize / 2, sy)               // 좌측
                },
                Fill = Brushes.Transparent,
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 33, 150, 243)), // Blue #2196F3
                StrokeThickness = 3 * inverseScale
            };
            ApiMarkersCanvas.Children.Add(apiOutline);

            // API 마커가 매핑 가능한 타입이면 아이콘 사용
            var mappedType = marker.MappedMarkerType;
            BitmapImage? iconImage = null;

            if (mappedType.HasValue)
            {
                iconImage = GetMarkerIcon(mappedType.Value);
            }

            if (iconImage != null && mappedType.HasValue)
            {
                // 아이콘 사용
                var image = new Image
                {
                    Source = iconImage,
                    Width = markerSize,
                    Height = markerSize,
                    Opacity = opacity,
                    Tag = marker.Uid
                };

                Canvas.SetLeft(image, sx - markerSize / 2);
                Canvas.SetTop(image, sy - markerSize / 2);
                ApiMarkersCanvas.Children.Add(image);

                // 선택된 경우 추가 테두리
                if (isSelected)
                {
                    var selectionDiamond = new Polygon
                    {
                        Points = new PointCollection
                        {
                            new Point(sx, sy - (diamondSize + 8 * inverseScale) / 2),
                            new Point(sx + (diamondSize + 8 * inverseScale) / 2, sy),
                            new Point(sx, sy + (diamondSize + 8 * inverseScale) / 2),
                            new Point(sx - (diamondSize + 8 * inverseScale) / 2, sy)
                        },
                        Fill = Brushes.Transparent,
                        Stroke = new SolidColorBrush(Color.FromRgb(0, 188, 212)), // Cyan
                        StrokeThickness = 3 * inverseScale
                    };
                    ApiMarkersCanvas.Children.Add(selectionDiamond);
                }
            }
            else
            {
                // 아이콘이 없는 경우 (Quests, Loot 등) 카테고리 표시
                var markerColor = isSelected
                    ? Color.FromArgb((byte)(opacity * 255), 0, 188, 212)  // Cyan
                    : Color.FromArgb((byte)(opacity * 255), 33, 150, 243); // Blue

                var rect = new Rectangle
                {
                    Width = markerSize,
                    Height = markerSize,
                    Fill = new SolidColorBrush(markerColor),
                    Stroke = isSelected ? Brushes.White : new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 200, 200, 200)),
                    StrokeThickness = (isSelected ? 3 : 2) * inverseScale,
                    RadiusX = 4 * inverseScale,
                    RadiusY = 4 * inverseScale,
                    Tag = marker.Uid
                };

                Canvas.SetLeft(rect, sx - markerSize / 2);
                Canvas.SetTop(rect, sy - markerSize / 2);
                ApiMarkersCanvas.Children.Add(rect);

                // Category indicator
                var categoryText = marker.Category switch
                {
                    "Extractions" => "E",
                    "Spawns" => "S",
                    "Quests" => "Q",
                    "Keys" => "K",
                    "Loot" => "L",
                    "Miscellaneous" => "M",
                    _ => "?"
                };

                var catLabel = new TextBlock
                {
                    Text = categoryText,
                    Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                    FontSize = 20 * inverseScale,
                    FontWeight = FontWeights.Bold
                };

                catLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(catLabel, sx - catLabel.DesiredSize.Width / 2);
                Canvas.SetTop(catLabel, sy - catLabel.DesiredSize.Height / 2);
                ApiMarkersCanvas.Children.Add(catLabel);
            }

            // "API" 배지 (마커 우측 상단)
            var apiBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 230), 33, 150, 243)), // Blue
                CornerRadius = new CornerRadius(3 * inverseScale),
                Padding = new Thickness(3 * inverseScale, 1 * inverseScale, 3 * inverseScale, 1 * inverseScale),
                Child = new TextBlock
                {
                    Text = "API",
                    Foreground = Brushes.White,
                    FontSize = 12 * inverseScale,
                    FontWeight = FontWeights.Bold
                }
            };
            apiBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(apiBadge, sx + markerSize / 2 - 4 * inverseScale);
            Canvas.SetTop(apiBadge, sy - markerSize / 2 - 8 * inverseScale);
            ApiMarkersCanvas.Children.Add(apiBadge);

            // Name label
            var (r, g, b) = mappedType.HasValue
                ? MapMarker.GetMarkerColor(mappedType.Value)
                : ((byte)33, (byte)150, (byte)243);

            var nameColor = Color.FromArgb((byte)(opacity * 255), r, g, b);

            var nameLabel = new TextBlock
            {
                Text = marker.Name.Length > 35 ? marker.Name.Substring(0, 32) + "..." : marker.Name,
                Foreground = new SolidColorBrush(nameColor),
                FontSize = 20 * inverseScale,
                FontWeight = FontWeights.SemiBold
            };

            Canvas.SetLeft(nameLabel, sx + markerSize / 2 + 8 * inverseScale);
            Canvas.SetTop(nameLabel, sy - 10 * inverseScale);
            ApiMarkersCanvas.Children.Add(nameLabel);

            // Floor label (if different floor and transform applied)
            if (hasFloors && marker.FloorId != null && opacity < 1.0)
            {
                var floorDisplayName = _sortedFloors?
                    .FirstOrDefault(f => string.Equals(f.LayerId, marker.FloorId, StringComparison.OrdinalIgnoreCase))
                    ?.DisplayName ?? marker.FloorId;

                var floorLabel = new TextBlock
                {
                    Text = $"[{floorDisplayName}]",
                    Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 200), 154, 136, 102)),
                    FontSize = 16 * inverseScale,
                    FontStyle = FontStyles.Italic
                };

                Canvas.SetLeft(floorLabel, sx + markerSize / 2 + 8 * inverseScale);
                Canvas.SetTop(floorLabel, sy + 12 * inverseScale);
                ApiMarkersCanvas.Children.Add(floorLabel);
            }
        }
    }

    private void DrawMatchLines()
    {
        if (MatchLinesCanvas == null || _currentMapConfig == null) return;

        var inverseScale = 1.0 / _zoomLevel;

        foreach (var match in _matchResults)
        {
            // Geometry가 없는 마커는 스킵
            if (match.ApiMarker.Geometry == null) continue;

            // 수정된 위치가 있으면 사용
            double dbGameX = match.DbMarker.X;
            double dbGameZ = match.DbMarker.Z;
            if (_modifiedDbPositions.TryGetValue(match.DbMarker.Id, out var modifiedPos))
            {
                dbGameX = modifiedPos.X;
                dbGameZ = modifiedPos.Z;
            }

            var (dbSx, dbSy) = _currentMapConfig.GameToScreen(dbGameX, dbGameZ);

            double apiSx, apiSy;
            if (match.ApiMarker.GameX.HasValue && match.ApiMarker.GameZ.HasValue)
            {
                (apiSx, apiSy) = _currentMapConfig.GameToScreen(match.ApiMarker.GameX.Value, match.ApiMarker.GameZ.Value);
            }
            else
            {
                apiSx = match.ApiMarker.Geometry.X * 10 + _currentMapConfig.ImageWidth / 2;
                apiSy = match.ApiMarker.Geometry.Y * 10 + _currentMapConfig.ImageHeight / 2;
            }

            // Reference point: yellow, others: orange
            var lineColor = match.IsReferencePoint
                ? Color.FromRgb(255, 193, 7)   // Yellow
                : Color.FromRgb(255, 152, 0);  // Orange

            var line = new Line
            {
                X1 = dbSx,
                Y1 = dbSy,
                X2 = apiSx,
                Y2 = apiSy,
                Stroke = new SolidColorBrush(lineColor),
                StrokeThickness = 2 * inverseScale,
                StrokeDashArray = match.IsReferencePoint ? null : new DoubleCollection { 4, 2 }
            };

            MatchLinesCanvas.Children.Add(line);

            // Error label if transform applied
            if (match.DistanceError.HasValue)
            {
                var midX = (dbSx + apiSx) / 2;
                var midY = (dbSy + apiSy) / 2;

                var errorColor = match.DistanceError.Value < 10 ? Colors.LimeGreen :
                                match.DistanceError.Value < 30 ? Colors.Yellow :
                                Colors.Red;

                var errorLabel = new TextBlock
                {
                    Text = $"{match.DistanceError.Value:F1}",
                    Foreground = new SolidColorBrush(errorColor),
                    FontSize = 14 * inverseScale,
                    Background = new SolidColorBrush(Color.FromArgb(180, 30, 30, 30))
                };

                Canvas.SetLeft(errorLabel, midX);
                Canvas.SetTop(errorLabel, midY);
                MatchLinesCanvas.Children.Add(errorLabel);
            }
        }
    }

    #endregion

    #region Zoom and Pan

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
    {
        var nextPreset = ZoomPresets.FirstOrDefault(p => p > _zoomLevel);
        var newZoom = nextPreset > 0 ? nextPreset : _zoomLevel * 1.25;
        ZoomToCenter(newZoom);
    }

    private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
    {
        var prevPreset = ZoomPresets.LastOrDefault(p => p < _zoomLevel);
        var newZoom = prevPreset > 0 ? prevPreset : _zoomLevel * 0.8;
        ZoomToCenter(newZoom);
    }

    private void BtnResetView_Click(object sender, RoutedEventArgs e)
    {
        SetZoom(1.0);
        CenterMapInView();
    }

    private void ZoomToCenter(double newZoom)
    {
        var mousePos = new Point(MapViewerGrid.ActualWidth / 2, MapViewerGrid.ActualHeight / 2);
        ZoomToPoint(newZoom, mousePos);
    }

    private void ZoomToPoint(double newZoom, Point viewerPoint)
    {
        var oldZoom = _zoomLevel;
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);

        if (Math.Abs(newZoom - oldZoom) < 0.001) return;

        var canvasX = (viewerPoint.X - MapTranslate.X) / oldZoom;
        var canvasY = (viewerPoint.Y - MapTranslate.Y) / oldZoom;

        MapTranslate.X = viewerPoint.X - canvasX * newZoom;
        MapTranslate.Y = viewerPoint.Y - canvasY * newZoom;

        SetZoom(newZoom);
    }

    private void SetZoom(double zoom)
    {
        _zoomLevel = Math.Clamp(zoom, MinZoom, MaxZoom);
        MapScale.ScaleX = _zoomLevel;
        MapScale.ScaleY = _zoomLevel;

        var zoomPercent = $"{_zoomLevel * 100:F0}%";
        ZoomText.Text = zoomPercent;

        RedrawAll();
    }

    private void CenterMapInView()
    {
        var viewerWidth = MapViewerGrid.ActualWidth;
        var viewerHeight = MapViewerGrid.ActualHeight;

        if (viewerWidth <= 0 || viewerHeight <= 0)
        {
            Dispatcher.BeginInvoke(new Action(CenterMapInView), System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        var scaledMapWidth = MapCanvas.Width * _zoomLevel;
        var scaledMapHeight = MapCanvas.Height * _zoomLevel;

        MapTranslate.X = (viewerWidth - scaledMapWidth) / 2;
        MapTranslate.Y = (viewerHeight - scaledMapHeight) / 2;
    }

    #endregion

    #region Mouse Events

    private void MapViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Ctrl+클릭: DB 마커 드래그
        if (Keyboard.Modifiers == ModifierKeys.Control && _currentMapConfig != null)
        {
            var dbPos = e.GetPosition(DbMarkersCanvas);
            var dbHitResult = VisualTreeHelper.HitTest(DbMarkersCanvas, dbPos);

            // Ellipse (테두리)에서 Tag 확인
            if (dbHitResult?.VisualHit is Ellipse ellipse && ellipse.Tag is string markerId)
            {
                var marker = _dbMarkers.FirstOrDefault(m => m.Id == markerId);
                if (marker != null)
                {
                    _isDraggingDbMarker = true;
                    _draggingDbMarker = marker;
                    _dbMarkerDragStart = e.GetPosition(MapCanvas);
                    MapViewerGrid.CaptureMouse();
                    MapCanvas.Cursor = Cursors.SizeAll;
                    return;
                }
            }
        }

        // API 마커 클릭 체크
        var pos = e.GetPosition(ApiMarkersCanvas);
        var hitResult = VisualTreeHelper.HitTest(ApiMarkersCanvas, pos);

        string? uid = null;

        // Rectangle 또는 Image에서 Tag 확인
        if (hitResult?.VisualHit is Rectangle rect && rect.Tag is string rectUid)
        {
            uid = rectUid;
        }
        else if (hitResult?.VisualHit is Image img && img.Tag is string imgUid)
        {
            uid = imgUid;
        }

        if (uid != null)
        {
            // 마커 선택/해제 토글
            if (_selectedApiMarkers.Contains(uid))
            {
                _selectedApiMarkers.Remove(uid);
            }
            else
            {
                _selectedApiMarkers.Add(uid);
            }

            UpdateCounts();
            UpdateButtonStates();
            RedrawAll();
            return;
        }

        // 드래그 시작
        _isDragging = true;
        _dragStartPoint = e.GetPosition(MapViewerGrid);
        _dragStartTranslateX = MapTranslate.X;
        _dragStartTranslateY = MapTranslate.Y;
        MapViewerGrid.CaptureMouse();
    }

    private void MapViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // DB 마커 드래그 종료
        if (_isDraggingDbMarker && _draggingDbMarker != null && _currentMapConfig != null)
        {
            var canvasPos = e.GetPosition(MapCanvas);
            var (newGameX, newGameZ) = _currentMapConfig.ScreenToGame(canvasPos.X, canvasPos.Y);

            // 수정된 위치 저장
            _modifiedDbPositions[_draggingDbMarker.Id] = (newGameX, newGameZ);

            _isDraggingDbMarker = false;
            _draggingDbMarker = null;
            MapViewerGrid.ReleaseMouseCapture();
            MapCanvas.Cursor = Cursors.Arrow;

            UpdateModifiedCount();
            RedrawAll();
            return;
        }

        _isDragging = false;
        MapViewerGrid.ReleaseMouseCapture();
        MapCanvas.Cursor = Cursors.Arrow;
    }

    private void MapViewer_MouseMove(object sender, MouseEventArgs e)
    {
        // Update coordinate display
        if (_currentMapConfig != null)
        {
            var canvasPos = e.GetPosition(MapCanvas);
            var (gameX, gameZ) = _currentMapConfig.ScreenToGame(canvasPos.X, canvasPos.Y);
            GameCoordsText.Text = $"X: {gameX:F1}, Z: {gameZ:F1}";
        }

        // DB 마커 드래그 중
        if (_isDraggingDbMarker && _draggingDbMarker != null && _currentMapConfig != null)
        {
            var canvasPos = e.GetPosition(MapCanvas);
            var (newGameX, newGameZ) = _currentMapConfig.ScreenToGame(canvasPos.X, canvasPos.Y);

            // 임시로 수정된 위치 저장하고 다시 그리기
            _modifiedDbPositions[_draggingDbMarker.Id] = (newGameX, newGameZ);
            RedrawAll();
            return;
        }

        if (!_isDragging) return;

        var currentPt = e.GetPosition(MapViewerGrid);
        var deltaX = currentPt.X - _dragStartPoint.X;
        var deltaY = currentPt.Y - _dragStartPoint.Y;

        MapTranslate.X = _dragStartTranslateX + deltaX;
        MapTranslate.Y = _dragStartTranslateY + deltaY;
        MapCanvas.Cursor = Cursors.ScrollAll;
    }

    private void MapViewer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mousePos = e.GetPosition(MapViewerGrid);
        var zoomFactor = e.Delta > 0 ? 1.15 : 0.87;
        var newZoom = Math.Clamp(_zoomLevel * zoomFactor, MinZoom, MaxZoom);

        ZoomToPoint(newZoom, mousePos);
        e.Handled = true;
    }

    private void MapViewer_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 매칭된 마커의 참조점 토글
        var pos = e.GetPosition(MatchLinesCanvas);

        foreach (var match in _matchResults)
        {
            var (dbSx, dbSy) = _currentMapConfig!.GameToScreen(match.DbMarker.X, match.DbMarker.Z);

            var dist = Math.Sqrt(Math.Pow(pos.X - dbSx, 2) + Math.Pow(pos.Y - dbSy, 2));
            if (dist < 30 / _zoomLevel)
            {
                match.IsReferencePoint = !match.IsReferencePoint;
                UpdateTransformInfo();
                UpdateButtonStates();
                RedrawAll();
                return;
            }
        }
    }

    #endregion
}
