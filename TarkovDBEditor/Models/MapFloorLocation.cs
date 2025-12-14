using System.ComponentModel;

namespace TarkovDBEditor.Models;

/// <summary>
/// 맵에서 Y 좌표 (및 선택적 XZ 좌표)에 따라 Floor를 자동 감지하기 위한 설정 모델.
/// </summary>
public class MapFloorLocation : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private string _mapKey = string.Empty;
    private string _floorId = string.Empty;
    private string _regionName = string.Empty;
    private double _minY;
    private double _maxY;
    private double? _minX;
    private double? _maxX;
    private double? _minZ;
    private double? _maxZ;
    private int _priority;
    private DateTime _createdAt = DateTime.UtcNow;
    private DateTime _updatedAt = DateTime.UtcNow;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 고유 ID
    /// </summary>
    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(nameof(Id)); }
    }

    /// <summary>
    /// 맵 키 (Customs, Factory 등)
    /// </summary>
    public string MapKey
    {
        get => _mapKey;
        set { _mapKey = value; OnPropertyChanged(nameof(MapKey)); }
    }

    /// <summary>
    /// Floor Layer ID (main, basement, level2 등)
    /// </summary>
    public string FloorId
    {
        get => _floorId;
        set { _floorId = value; OnPropertyChanged(nameof(FloorId)); }
    }

    /// <summary>
    /// 영역 이름 (설명용)
    /// </summary>
    public string RegionName
    {
        get => _regionName;
        set { _regionName = value; OnPropertyChanged(nameof(RegionName)); }
    }

    /// <summary>
    /// 최소 Y 좌표 (높이)
    /// </summary>
    public double MinY
    {
        get => _minY;
        set { _minY = value; OnPropertyChanged(nameof(MinY)); }
    }

    /// <summary>
    /// 최대 Y 좌표 (높이)
    /// </summary>
    public double MaxY
    {
        get => _maxY;
        set { _maxY = value; OnPropertyChanged(nameof(MaxY)); }
    }

    /// <summary>
    /// 최소 X 좌표 (선택적 - 특정 영역 지정용)
    /// </summary>
    public double? MinX
    {
        get => _minX;
        set { _minX = value; OnPropertyChanged(nameof(MinX)); OnPropertyChanged(nameof(HasXZBounds)); }
    }

    /// <summary>
    /// 최대 X 좌표 (선택적 - 특정 영역 지정용)
    /// </summary>
    public double? MaxX
    {
        get => _maxX;
        set { _maxX = value; OnPropertyChanged(nameof(MaxX)); OnPropertyChanged(nameof(HasXZBounds)); }
    }

    /// <summary>
    /// 최소 Z 좌표 (선택적 - 특정 영역 지정용)
    /// </summary>
    public double? MinZ
    {
        get => _minZ;
        set { _minZ = value; OnPropertyChanged(nameof(MinZ)); OnPropertyChanged(nameof(HasXZBounds)); }
    }

    /// <summary>
    /// 최대 Z 좌표 (선택적 - 특정 영역 지정용)
    /// </summary>
    public double? MaxZ
    {
        get => _maxZ;
        set { _maxZ = value; OnPropertyChanged(nameof(MaxZ)); OnPropertyChanged(nameof(HasXZBounds)); }
    }

    /// <summary>
    /// 우선순위 (높을수록 먼저 체크)
    /// </summary>
    public int Priority
    {
        get => _priority;
        set { _priority = value; OnPropertyChanged(nameof(Priority)); }
    }

    /// <summary>
    /// 생성 시간
    /// </summary>
    public DateTime CreatedAt
    {
        get => _createdAt;
        set { _createdAt = value; OnPropertyChanged(nameof(CreatedAt)); }
    }

    /// <summary>
    /// 수정 시간
    /// </summary>
    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set { _updatedAt = value; OnPropertyChanged(nameof(UpdatedAt)); }
    }

    /// <summary>
    /// XZ 범위가 지정되어 있는지 여부
    /// </summary>
    public bool HasXZBounds => MinX.HasValue && MaxX.HasValue && MinZ.HasValue && MaxZ.HasValue;

    /// <summary>
    /// 좌표가 이 Floor 영역에 해당하는지 확인
    /// </summary>
    public bool Contains(double x, double y, double z)
    {
        // Y 범위 확인 (필수)
        if (y < MinY || y > MaxY)
            return false;

        // XZ 범위 확인 (선택적)
        if (HasXZBounds)
        {
            if (x < MinX!.Value || x > MaxX!.Value)
                return false;
            if (z < MinZ!.Value || z > MaxZ!.Value)
                return false;
        }

        return true;
    }

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public override string ToString()
    {
        var xzStr = HasXZBounds ? $" X:[{MinX:F0}~{MaxX:F0}] Z:[{MinZ:F0}~{MaxZ:F0}]" : "";
        return $"{RegionName} ({FloorId}) Y:[{MinY:F1}~{MaxY:F1}]{xzStr} P:{Priority}";
    }
}
