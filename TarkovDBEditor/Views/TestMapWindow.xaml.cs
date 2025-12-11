using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Clipboard = System.Windows.Clipboard;
using Cursors = System.Windows.Input.Cursors;

namespace TarkovDBEditor.Views;

public partial class TestMapWindow : Window
{
    // Lighthouse map coordinate transformation constants
    private const double MapWidth = 3100;
    private const double MapHeight = 3700;

    // Lighthouse map coordinate transformation (derived from 2 actual marker data points)
    private const double ScaleX = -1.0;
    private const double OffsetX = 1550.0;
    private const double ScaleZ = 1.0;
    private const double OffsetZ = 2050.0;

    // Zoom settings
    private double _zoomLevel = 1.0;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 5.0;
    private static readonly double[] ZoomPresets = { 0.1, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0, 5.0 };

    // Drag state
    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartTranslateX;
    private double _dragStartTranslateY;

    // Current marker data
    private double _gameX, _gameY, _gameZ;
    private double _qx, _qy, _qz, _qw;

    public TestMapWindow()
    {
        InitializeComponent();
        Loaded += TestMapWindow_Loaded;
    }

    private void TestMapWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadSvgMap();

        // Initial marker position
        ApplyScreenshotData(ScreenshotInput.Text);

        // Center map after loaded
        Dispatcher.BeginInvoke(new Action(CenterMapInView), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void LoadSvgMap()
    {
        try
        {
            // Load SVG from file
            var svgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "LighthouseMap.svg");

            if (!File.Exists(svgPath))
            {
                MessageBox.Show($"Could not find SVG file: {svgPath}",
                    "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Load SVG using SvgViewbox (vector graphics - no quality loss on zoom)
            MapSvg.Source = new Uri(svgPath, UriKind.Absolute);
            MapSvg.Width = MapWidth;
            MapSvg.Height = MapHeight;

            MapCanvas.Width = MapWidth;
            MapCanvas.Height = MapHeight;
            Canvas.SetLeft(MapSvg, 0);
            Canvas.SetTop(MapSvg, 0);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load SVG map: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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

    /// <summary>
    /// Zoom to the center of the viewer.
    /// </summary>
    private void ZoomToCenter(double newZoom)
    {
        var mousePos = new Point(MapViewerGrid.ActualWidth / 2, MapViewerGrid.ActualHeight / 2);
        ZoomToPoint(newZoom, mousePos);
    }

    /// <summary>
    /// Zoom to a specific point (mouse position).
    /// </summary>
    private void ZoomToPoint(double newZoom, Point viewerPoint)
    {
        var oldZoom = _zoomLevel;
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);

        if (Math.Abs(newZoom - oldZoom) < 0.001) return;

        // Calculate canvas position under mouse
        var canvasX = (viewerPoint.X - MapTranslate.X) / oldZoom;
        var canvasY = (viewerPoint.Y - MapTranslate.Y) / oldZoom;

        // Adjust translate so the same canvas point stays under mouse after zoom
        MapTranslate.X = viewerPoint.X - canvasX * newZoom;
        MapTranslate.Y = viewerPoint.Y - canvasY * newZoom;

        SetZoom(newZoom);
    }

    private void SetZoom(double zoom)
    {
        _zoomLevel = Math.Clamp(zoom, MinZoom, MaxZoom);
        MapScale.ScaleX = _zoomLevel;
        MapScale.ScaleY = _zoomLevel;

        // Update zoom display
        var zoomPercent = $"{_zoomLevel * 100:F0}%";
        ZoomText.Text = zoomPercent;
        ZoomStatusText.Text = zoomPercent;

        // Update marker scale to keep fixed size
        UpdateMarkerScale();
    }

    private void UpdateMarkerScale()
    {
        var inverseScale = 1.0 / _zoomLevel;
        MarkerScale.ScaleX = inverseScale;
        MarkerScale.ScaleY = inverseScale;
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
        if (e.ClickCount == 2)
        {
            // Double click to set marker
            var pos = e.GetPosition(MapCanvas);
            var (gameX, gameZ) = MapToGameCoordinates(pos.X, pos.Y);

            _gameX = gameX;
            _gameZ = gameZ;
            PositionText.Text = $"X: {_gameX:F2}, Y: {_gameY:F2}, Z: {_gameZ:F2}";
            MapCoordsText.Text = $"Map: ({pos.X:F0}, {pos.Y:F0})";

            UpdateMarkerPosition(pos.X, pos.Y, MarkerRotation.Angle);
            PlayerMarker.Visibility = Visibility.Visible;
            return;
        }

        // Start dragging
        _isDragging = true;
        _dragStartPoint = e.GetPosition(MapViewerGrid);
        _dragStartTranslateX = MapTranslate.X;
        _dragStartTranslateY = MapTranslate.Y;
        MapViewerGrid.CaptureMouse();
        MapCanvas.Cursor = Cursors.ScrollAll;
    }

    private void MapViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            MapViewerGrid.ReleaseMouseCapture();
            MapCanvas.Cursor = Cursors.Arrow;
        }
    }

    private void MapViewer_MouseMove(object sender, MouseEventArgs e)
    {
        // Update status bar with current position
        var canvasPos = e.GetPosition(MapCanvas);
        var (gameX, gameZ) = MapToGameCoordinates(canvasPos.X, canvasPos.Y);
        MapPositionText.Text = $"Game: X={gameX:F1}, Z={gameZ:F1} | Map: ({canvasPos.X:F0}, {canvasPos.Y:F0})";

        if (!_isDragging) return;

        var currentPoint = e.GetPosition(MapViewerGrid);
        var deltaX = currentPoint.X - _dragStartPoint.X;
        var deltaY = currentPoint.Y - _dragStartPoint.Y;

        MapTranslate.X = _dragStartTranslateX + deltaX;
        MapTranslate.Y = _dragStartTranslateY + deltaY;
    }

    private void MapViewer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mousePos = e.GetPosition(MapViewerGrid);
        var zoomFactor = e.Delta > 0 ? 1.15 : 0.87;
        var newZoom = Math.Clamp(_zoomLevel * zoomFactor, MinZoom, MaxZoom);

        ZoomToPoint(newZoom, mousePos);
        e.Handled = true;
    }

    #endregion

    #region Screenshot Parsing

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyScreenshotData(ScreenshotInput.Text);
    }

    private void ParseClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            ScreenshotInput.Text = text;
            ApplyScreenshotData(text);
        }
    }

    private void ApplyScreenshotData(string screenshotFilename)
    {
        if (!ParseScreenshotFilename(screenshotFilename, out var position, out var quaternion))
        {
            MessageBox.Show("Failed to parse screenshot filename.\n\nExpected format:\n[date]_X, Y, Z_qx, qy, qz, qw_...",
                "Parse Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _gameX = position.x;
        _gameY = position.y;
        _gameZ = position.z;
        _qx = quaternion.x;
        _qy = quaternion.y;
        _qz = quaternion.z;
        _qw = quaternion.w;

        // Update display
        PositionText.Text = $"X: {_gameX:F2}, Y: {_gameY:F2}, Z: {_gameZ:F2}";
        QuaternionText.Text = $"qx: {_qx:F5}, qy: {_qy:F5}, qz: {_qz:F5}, qw: {_qw:F5}";

        // Calculate yaw from quaternion
        var yawDegrees = QuaternionToYaw(_qx, _qy, _qz, _qw);
        YawText.Text = $"{yawDegrees:F1}";

        // Convert game coordinates to map coordinates
        var (mapX, mapY) = GameToMapCoordinates(_gameX, _gameZ);

        // Update marker position
        UpdateMarkerPosition(mapX, mapY, yawDegrees);

        // Show marker
        PlayerMarker.Visibility = Visibility.Visible;

        // Update map coords display
        MapCoordsText.Text = $"Map: ({mapX:F0}, {mapY:F0})";
    }

    private bool ParseScreenshotFilename(string filename,
        out (double x, double y, double z) position,
        out (double x, double y, double z, double w) quaternion)
    {
        position = (0, 0, 0);
        quaternion = (0, 0, 0, 1);

        // Pattern: date_position_quaternion_extra
        var pattern = @"_(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)_(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)_";
        var match = Regex.Match(filename, pattern);

        if (!match.Success) return false;

        try
        {
            position = (
                double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)
            );

            quaternion = (
                double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[7].Value, CultureInfo.InvariantCulture)
            );

            return true;
        }
        catch
        {
            return false;
        }
    }

    private double QuaternionToYaw(double qx, double qy, double qz, double qw)
    {
        var sinyCosp = 2.0 * (qw * qy + qx * qz);
        var cosyCosp = 1.0 - 2.0 * (qy * qy + qz * qz);
        var yawRadians = Math.Atan2(sinyCosp, cosyCosp);
        var yawDegrees = yawRadians * (180.0 / Math.PI);

        // Add 180 degrees to match tarkov-market map orientation
        yawDegrees += 180;

        // Normalize to 0-360
        if (yawDegrees < 0) yawDegrees += 360;
        if (yawDegrees >= 360) yawDegrees -= 360;

        return yawDegrees;
    }

    #endregion

    #region Coordinate Conversion

    private (double mapX, double mapY) GameToMapCoordinates(double gameX, double gameZ)
    {
        var mapX = gameX * ScaleX + OffsetX;
        var mapY = gameZ * ScaleZ + OffsetZ;

        mapX = Math.Clamp(mapX, 0, MapWidth);
        mapY = Math.Clamp(mapY, 0, MapHeight);

        return (mapX, mapY);
    }

    private (double gameX, double gameZ) MapToGameCoordinates(double mapX, double mapY)
    {
        var gameX = (mapX - OffsetX) / ScaleX;
        var gameZ = (mapY - OffsetZ) / ScaleZ;
        return (gameX, gameZ);
    }

    private void UpdateMarkerPosition(double mapX, double mapY, double yawDegrees)
    {
        MarkerTranslation.X = mapX;
        MarkerTranslation.Y = mapY;
        MarkerRotation.Angle = yawDegrees;
    }

    #endregion
}
