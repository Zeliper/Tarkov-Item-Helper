using System.Text.Json.Serialization;

namespace TarkovHelper.Models;

/// <summary>
/// 퀘스트 진행 상태 엔트리 (이중 키 - ID + NormalizedName)
/// DB 업데이트 시에도 매핑을 유지할 수 있도록 두 키를 모두 저장합니다.
/// </summary>
public class QuestProgressEntry
{
    /// <summary>
    /// tarkov.dev 퀘스트 ID (가장 안정적인 식별자)
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// 퀘스트 NormalizedName (레거시 호환성)
    /// </summary>
    [JsonPropertyName("normalizedName")]
    public string? NormalizedName { get; set; }

    /// <summary>
    /// 퀘스트 상태
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "Done";

    /// <summary>
    /// 유효한 엔트리인지 확인 (ID 또는 NormalizedName 중 하나라도 있어야 함)
    /// </summary>
    [JsonIgnore]
    public bool IsValid => !string.IsNullOrEmpty(Id) || !string.IsNullOrEmpty(NormalizedName);

    /// <summary>
    /// 매핑용 키 반환 (ID 우선, 없으면 NormalizedName)
    /// </summary>
    [JsonIgnore]
    public string? PrimaryKey => !string.IsNullOrEmpty(Id) ? Id : NormalizedName;
}

/// <summary>
/// 퀘스트 진행 상태 데이터 (새 형식 - 이중 키)
/// </summary>
public class QuestProgressDataV2
{
    /// <summary>
    /// 데이터 형식 버전
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 2;

    /// <summary>
    /// 완료된 퀘스트 목록
    /// </summary>
    [JsonPropertyName("completedQuests")]
    public List<QuestProgressEntry> CompletedQuests { get; set; } = new();

    /// <summary>
    /// 실패한 퀘스트 목록
    /// </summary>
    [JsonPropertyName("failedQuests")]
    public List<QuestProgressEntry> FailedQuests { get; set; } = new();

    /// <summary>
    /// 특정 퀘스트가 완료되었는지 확인 (ID 또는 NormalizedName으로)
    /// </summary>
    public bool IsCompleted(string? id, string? normalizedName)
    {
        return CompletedQuests.Any(e =>
            (!string.IsNullOrEmpty(id) && e.Id == id) ||
            (!string.IsNullOrEmpty(normalizedName) &&
             string.Equals(e.NormalizedName, normalizedName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// 특정 퀘스트가 실패했는지 확인 (ID 또는 NormalizedName으로)
    /// </summary>
    public bool IsFailed(string? id, string? normalizedName)
    {
        return FailedQuests.Any(e =>
            (!string.IsNullOrEmpty(id) && e.Id == id) ||
            (!string.IsNullOrEmpty(normalizedName) &&
             string.Equals(e.NormalizedName, normalizedName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// 퀘스트를 완료 목록에 추가 (기존 엔트리 업데이트)
    /// </summary>
    public void MarkCompleted(string? id, string? normalizedName)
    {
        // 실패 목록에서 제거
        RemoveFromFailed(id, normalizedName);

        // 이미 완료 목록에 있으면 업데이트
        var existing = FindEntry(CompletedQuests, id, normalizedName);
        if (existing != null)
        {
            // ID나 NormalizedName이 새로 제공되면 업데이트
            if (!string.IsNullOrEmpty(id) && string.IsNullOrEmpty(existing.Id))
                existing.Id = id;
            if (!string.IsNullOrEmpty(normalizedName) && string.IsNullOrEmpty(existing.NormalizedName))
                existing.NormalizedName = normalizedName;
            return;
        }

        // 새 엔트리 추가
        CompletedQuests.Add(new QuestProgressEntry
        {
            Id = id,
            NormalizedName = normalizedName,
            Status = "Done"
        });
    }

    /// <summary>
    /// 퀘스트를 실패 목록에 추가 (기존 엔트리 업데이트)
    /// </summary>
    public void MarkFailed(string? id, string? normalizedName)
    {
        // 완료 목록에서 제거
        RemoveFromCompleted(id, normalizedName);

        // 이미 실패 목록에 있으면 업데이트
        var existing = FindEntry(FailedQuests, id, normalizedName);
        if (existing != null)
        {
            if (!string.IsNullOrEmpty(id) && string.IsNullOrEmpty(existing.Id))
                existing.Id = id;
            if (!string.IsNullOrEmpty(normalizedName) && string.IsNullOrEmpty(existing.NormalizedName))
                existing.NormalizedName = normalizedName;
            return;
        }

        // 새 엔트리 추가
        FailedQuests.Add(new QuestProgressEntry
        {
            Id = id,
            NormalizedName = normalizedName,
            Status = "Failed"
        });
    }

    /// <summary>
    /// 퀘스트를 리셋 (완료/실패 목록에서 제거)
    /// </summary>
    public void Reset(string? id, string? normalizedName)
    {
        RemoveFromCompleted(id, normalizedName);
        RemoveFromFailed(id, normalizedName);
    }

    /// <summary>
    /// 모든 진행 상태 초기화
    /// </summary>
    public void ResetAll()
    {
        CompletedQuests.Clear();
        FailedQuests.Clear();
    }

    private QuestProgressEntry? FindEntry(List<QuestProgressEntry> list, string? id, string? normalizedName)
    {
        return list.FirstOrDefault(e =>
            (!string.IsNullOrEmpty(id) && e.Id == id) ||
            (!string.IsNullOrEmpty(normalizedName) &&
             string.Equals(e.NormalizedName, normalizedName, StringComparison.OrdinalIgnoreCase)));
    }

    private void RemoveFromCompleted(string? id, string? normalizedName)
    {
        CompletedQuests.RemoveAll(e =>
            (!string.IsNullOrEmpty(id) && e.Id == id) ||
            (!string.IsNullOrEmpty(normalizedName) &&
             string.Equals(e.NormalizedName, normalizedName, StringComparison.OrdinalIgnoreCase)));
    }

    private void RemoveFromFailed(string? id, string? normalizedName)
    {
        FailedQuests.RemoveAll(e =>
            (!string.IsNullOrEmpty(id) && e.Id == id) ||
            (!string.IsNullOrEmpty(normalizedName) &&
             string.Equals(e.NormalizedName, normalizedName, StringComparison.OrdinalIgnoreCase)));
    }
}

/// <summary>
/// 기존 quest_progress.json 형식 (V1 - 레거시)
/// NormalizedName만 저장하는 단순한 형식
/// </summary>
public class QuestProgressDataV1
{
    /// <summary>
    /// 완료된 퀘스트 (NormalizedName → Status)
    /// </summary>
    [JsonPropertyName("completedQuests")]
    public Dictionary<string, string>? CompletedQuests { get; set; }

    /// <summary>
    /// V2 형식으로 변환
    /// </summary>
    public QuestProgressDataV2 ToV2()
    {
        var v2 = new QuestProgressDataV2();

        if (CompletedQuests != null)
        {
            foreach (var kvp in CompletedQuests)
            {
                var status = kvp.Value;
                var entry = new QuestProgressEntry
                {
                    NormalizedName = kvp.Key,
                    Status = status
                };

                if (status == "Done")
                {
                    v2.CompletedQuests.Add(entry);
                }
                else if (status == "Failed")
                {
                    v2.FailedQuests.Add(entry);
                }
            }
        }

        return v2;
    }
}
