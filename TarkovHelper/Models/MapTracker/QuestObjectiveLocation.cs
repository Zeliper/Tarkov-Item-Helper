using System.Text.Json.Serialization;

namespace TarkovHelper.Models.MapTracker;

/// <summary>
/// tarkov.dev API의 TaskZone에서 가져온 퀘스트 목표 위치 정보.
/// 맵에 퀘스트 목표 마커를 표시하는 데 사용됩니다.
/// </summary>
public sealed class QuestObjectiveLocation
{
    /// <summary>
    /// 구역 ID (tarkov.dev API의 TaskZone.id)
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 맵 ID (tarkov.dev API의 map.id, 예: "5714dbc024597771384a510d" for Customs)
    /// </summary>
    [JsonPropertyName("mapId")]
    public string MapId { get; set; } = string.Empty;

    /// <summary>
    /// 맵 이름 (영문, 예: "Customs", "Woods")
    /// </summary>
    [JsonPropertyName("mapName")]
    public string MapName { get; set; } = string.Empty;

    /// <summary>
    /// 맵 normalized name (예: "customs", "woods")
    /// </summary>
    [JsonPropertyName("mapNormalizedName")]
    public string? MapNormalizedName { get; set; }

    /// <summary>
    /// 맵 한국어 이름
    /// </summary>
    [JsonPropertyName("mapNameKo")]
    public string? MapNameKo { get; set; }

    /// <summary>
    /// 중심 위치 X 좌표 (월드 좌표)
    /// </summary>
    [JsonPropertyName("x")]
    public double X { get; set; }

    /// <summary>
    /// 중심 위치 Y 좌표 (월드 좌표)
    /// </summary>
    [JsonPropertyName("y")]
    public double Y { get; set; }

    /// <summary>
    /// 중심 위치 Z 좌표 (높이)
    /// </summary>
    [JsonPropertyName("z")]
    public double? Z { get; set; }

    /// <summary>
    /// 구역 경계선 좌표 목록 (다각형 외곽선).
    /// tarkov.dev API의 outline 배열에서 변환.
    /// null이면 단일 포인트 마커로 표시.
    /// </summary>
    [JsonPropertyName("outline")]
    public List<OutlinePoint>? Outline { get; set; }

    /// <summary>
    /// 구역 상단 높이 (Z축)
    /// </summary>
    [JsonPropertyName("top")]
    public double? Top { get; set; }

    /// <summary>
    /// 구역 하단 높이 (Z축)
    /// </summary>
    [JsonPropertyName("bottom")]
    public double? Bottom { get; set; }

    /// <summary>
    /// 층 ID (예: "main", "basement", "floor2")
    /// </summary>
    [JsonPropertyName("floorId")]
    public string? FloorId { get; set; }
}

/// <summary>
/// 구역 외곽선의 한 점
/// </summary>
public sealed class OutlinePoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

/// <summary>
/// 퀘스트 목표와 위치 정보를 연결하는 모델.
/// 한 퀘스트의 한 목표에 대한 위치 정보를 담습니다.
/// </summary>
public sealed class TaskObjectiveWithLocation
{
    /// <summary>
    /// 목표 ID (tarkov.dev API의 TaskObjective.id)
    /// </summary>
    [JsonPropertyName("objectiveId")]
    public string ObjectiveId { get; set; } = string.Empty;

    /// <summary>
    /// 목표 설명 (영문)
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 목표 설명 (한국어)
    /// </summary>
    [JsonPropertyName("descriptionKo")]
    public string? DescriptionKo { get; set; }

    /// <summary>
    /// 목표 유형 (visit, mark, plantItem, extract, findItem 등)
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 연결된 퀘스트 normalized name
    /// </summary>
    [JsonPropertyName("taskNormalizedName")]
    public string TaskNormalizedName { get; set; } = string.Empty;

    /// <summary>
    /// 연결된 퀘스트 이름 (영문)
    /// </summary>
    [JsonPropertyName("taskName")]
    public string TaskName { get; set; } = string.Empty;

    /// <summary>
    /// 연결된 퀘스트 이름 (한국어)
    /// </summary>
    [JsonPropertyName("taskNameKo")]
    public string? TaskNameKo { get; set; }

    /// <summary>
    /// 이 목표의 위치들 (여러 맵/위치에 있을 수 있음)
    /// </summary>
    [JsonPropertyName("locations")]
    public List<QuestObjectiveLocation> Locations { get; set; } = new();

    /// <summary>
    /// 목표 진행 상태 (UI 표시용)
    /// </summary>
    [JsonIgnore]
    public bool IsCompleted { get; set; }

    /// <summary>
    /// 목표 인덱스 (Quests 탭과 연동용)
    /// </summary>
    [JsonIgnore]
    public int ObjectiveIndex { get; set; } = -1;

    /// <summary>
    /// 마커 색상 (목표 유형별 기본값 또는 사용자 설정)
    /// </summary>
    [JsonIgnore]
    public string MarkerColor => Type switch
    {
        "visit" => "#4CAF50",      // Green - 방문
        "mark" => "#FF9800",       // Orange - 마킹
        "plantItem" => "#9C27B0",  // Purple - 아이템 설치
        "extract" => "#2196F3",    // Blue - 탈출
        "findItem" => "#FFEB3B",   // Yellow - 아이템 찾기
        _ => "#607D8B"             // Gray - 기타
    };
}

/// <summary>
/// 맵별로 그룹화된 퀘스트 목표 위치 정보.
/// MapTrackerPage에서 현재 맵의 활성 퀘스트 목표를 표시하는 데 사용.
/// </summary>
public sealed class MapQuestObjectives
{
    /// <summary>
    /// 맵 이름 (영문)
    /// </summary>
    public string MapName { get; set; } = string.Empty;

    /// <summary>
    /// 맵 한국어 이름
    /// </summary>
    public string? MapNameKo { get; set; }

    /// <summary>
    /// 이 맵에서 완료해야 할 퀘스트 목표들
    /// </summary>
    public List<TaskObjectiveWithLocation> Objectives { get; set; } = new();
}
