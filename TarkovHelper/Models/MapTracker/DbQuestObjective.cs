using System.Text.Json;
using System.Text.Json.Serialization;

namespace TarkovHelper.Models.MapTracker;

/// <summary>
/// DB LocationPoints JSON에서 역직렬화되는 위치 포인트.
/// TarkovDBEditor의 LocationPoint와 동일한 구조.
/// </summary>
public sealed class DbLocationPoint
{
    [JsonPropertyName("X")]
    public double X { get; set; }

    /// <summary>
    /// 높이 (GameToScreen에서 사용 안 함)
    /// </summary>
    [JsonPropertyName("Y")]
    public double Y { get; set; }

    /// <summary>
    /// 수평 Z 좌표 (GameToScreen의 두 번째 파라미터)
    /// </summary>
    [JsonPropertyName("Z")]
    public double Z { get; set; }

    [JsonPropertyName("FloorId")]
    public string? FloorId { get; set; }
}

/// <summary>
/// DB QuestObjectives 테이블에서 로드된 퀘스트 목표.
/// TarkovDBEditor의 QuestObjectiveItem 구조를 반영.
/// </summary>
public sealed class DbQuestObjective
{
    public string Id { get; set; } = string.Empty;
    public string QuestId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? MapName { get; set; }
    public string? QuestLocation { get; set; }

    // 퀘스트 정보 (JOIN으로 가져옴)
    public string? QuestName { get; set; }
    public string? QuestNameKo { get; set; }

    /// <summary>
    /// 실제 사용할 맵 이름 (MapName이 있으면 MapName, 없으면 QuestLocation)
    /// </summary>
    public string? EffectiveMapName => !string.IsNullOrEmpty(MapName) ? MapName : QuestLocation;

    /// <summary>
    /// 좌표 포인트 목록 (다각형 영역 또는 단일/복수 포인트)
    /// </summary>
    public List<DbLocationPoint> LocationPoints { get; set; } = new();

    /// <summary>
    /// Optional 좌표 목록 (OR 관계 - 여러 위치 중 하나)
    /// </summary>
    public List<DbLocationPoint> OptionalPoints { get; set; } = new();

    /// <summary>
    /// 좌표가 있는지 여부
    /// </summary>
    public bool HasCoordinates => LocationPoints.Count > 0;

    /// <summary>
    /// Optional 좌표가 있는지 여부
    /// </summary>
    public bool HasOptionalPoints => OptionalPoints.Count > 0;

    /// <summary>
    /// JSON 문자열에서 LocationPoints 파싱
    /// </summary>
    public void ParseLocationPoints(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            LocationPoints = new List<DbLocationPoint>();
            return;
        }

        try
        {
            var points = JsonSerializer.Deserialize<List<DbLocationPoint>>(json);
            LocationPoints = points ?? new List<DbLocationPoint>();
        }
        catch
        {
            LocationPoints = new List<DbLocationPoint>();
        }
    }

    /// <summary>
    /// JSON 문자열에서 OptionalPoints 파싱
    /// </summary>
    public void ParseOptionalPoints(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            OptionalPoints = new List<DbLocationPoint>();
            return;
        }

        try
        {
            var points = JsonSerializer.Deserialize<List<DbLocationPoint>>(json);
            OptionalPoints = points ?? new List<DbLocationPoint>();
        }
        catch
        {
            OptionalPoints = new List<DbLocationPoint>();
        }
    }
}
