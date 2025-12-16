using System.IO;
using System.Text;
using System.Windows;
using TarkovHelper.Models;
using TarkovHelper.Services;

namespace TarkovHelper.Debug;

/// <summary>
/// Debug 모드에서 Toolbox 창에 표시될 테스트 함수들을 정의합니다.
/// [TestMenu] 어트리뷰트가 붙은 public 메서드가 버튼으로 표시됩니다.
/// </summary>
public static class TestMenu
{
    /// <summary>
    /// MainWindow 인스턴스 (Toolbox에서 주입)
    /// </summary>
    public static Window? MainWindow { get; set; }

    /// <summary>
    /// Health Care Privacy 퀘스트 동기화 디버그
    /// 로그 파일을 분석하여 왜 Part 3, 4가 Auto Complete 되는지 확인
    /// </summary>
    [TestMenu("Debug Health Care Privacy Sync")]
    public static async Task DebugHealthCarePrivacySyncAsync()
    {
        var logFolderPath = @"C:\Users\Zeliper\Downloads\Logs";
        var outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "hcp_sync_debug.txt");
        var sb = new StringBuilder();

        sb.AppendLine("=== Health Care Privacy Sync Debug ===");
        sb.AppendLine($"Log folder: {logFolderPath}");
        sb.AppendLine($"Generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // 1. DB 로드 확인
        var questDbService = QuestDbService.Instance;
        if (!questDbService.IsLoaded)
        {
            await questDbService.LoadQuestsAsync();
        }

        // QuestProgressService 초기화 (AllTasks를 사용하기 위해)
        var progressService = QuestProgressService.Instance;
        if (!progressService.IsLoadedFromDb)
        {
            await progressService.InitializeFromDbAsync();
        }

        // QuestGraphService 초기화
        var graphService = QuestGraphService.Instance;
        await graphService.InitializeAsync();

        sb.AppendLine($"Loaded {questDbService.QuestCount} quests from DB");
        sb.AppendLine();

        // 2. Health Care Privacy 퀘스트 정보 출력
        sb.AppendLine("=== Health Care Privacy Quest Info ===");
        var hcpQuests = new[] {
            "health-care-privacy---part-1",
            "health-care-privacy---part-2",
            "health-care-privacy---part-3",
            "health-care-privacy---part-4",
            "health-care-privacy---part-5",
            "health-care-privacy---part-6"
        };

        foreach (var hcpName in hcpQuests)
        {
            var quest = questDbService.GetQuestByNormalizedName(hcpName);
            if (quest != null)
            {
                sb.AppendLine($"{quest.Name}:");
                sb.AppendLine($"  IDs: {string.Join(", ", quest.Ids ?? new List<string>())}");
                sb.AppendLine($"  Previous: {string.Join(", ", quest.Previous ?? new List<string>())}");
                sb.AppendLine($"  LeadsTo: {string.Join(", ", quest.LeadsTo ?? new List<string>())}");
            }
        }
        sb.AppendLine();

        // 3. 로그 파일에서 모든 퀘스트 이벤트 파싱
        sb.AppendLine("=== Parsing Quest Events from Logs ===");
        var logSyncService = LogSyncService.Instance;
        var events = await logSyncService.ParseLogDirectoryAsync(logFolderPath);

        sb.AppendLine($"Total events found: {events.Count}");
        sb.AppendLine();

        // 4. Health Care Privacy 관련 이벤트 필터링
        sb.AppendLine("=== Health Care Privacy Related Events ===");

        // Build quest ID lookup
        var tasksByQuestId = new Dictionary<string, TarkovTask>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in progressService.AllTasks)
        {
            if (task.Ids != null)
            {
                foreach (var id in task.Ids)
                {
                    if (!string.IsNullOrEmpty(id) && !tasksByQuestId.ContainsKey(id))
                    {
                        tasksByQuestId[id] = task;
                    }
                }
            }
        }

        // HCP 퀘스트 ID 매핑
        var hcpIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hcpName in hcpQuests)
        {
            var quest = questDbService.GetQuestByNormalizedName(hcpName);
            if (quest?.Ids != null)
            {
                foreach (var id in quest.Ids)
                    hcpIds.Add(id);
            }
        }

        // HCP 관련 이벤트 출력
        var hcpEvents = events.Where(e => hcpIds.Contains(e.QuestId)).OrderBy(e => e.Timestamp).ToList();
        sb.AppendLine($"Health Care Privacy events: {hcpEvents.Count}");
        foreach (var evt in hcpEvents)
        {
            var task = tasksByQuestId.TryGetValue(evt.QuestId, out var t) ? t : null;
            sb.AppendLine($"  [{evt.Timestamp:MM/dd HH:mm}] {evt.EventType}: {task?.Name ?? evt.QuestId}");
        }
        sb.AppendLine();

        // 5. HCP를 prerequisite로 가지는 퀘스트들의 이벤트 확인
        sb.AppendLine("=== Quests that require Health Care Privacy ===");

        // Part 3를 prerequisite로 가지는 퀘스트 찾기
        var part3Name = "health-care-privacy---part-3";
        var part4Name = "health-care-privacy---part-4";

        sb.AppendLine("Quests requiring Part 3:");
        foreach (var task in progressService.AllTasks)
        {
            if (task.Previous?.Contains(part3Name, StringComparer.OrdinalIgnoreCase) == true)
            {
                sb.AppendLine($"  - {task.Name} (IDs: {string.Join(", ", task.Ids ?? new List<string>())})");
            }
        }

        sb.AppendLine("Quests requiring Part 4:");
        foreach (var task in progressService.AllTasks)
        {
            if (task.Previous?.Contains(part4Name, StringComparer.OrdinalIgnoreCase) == true)
            {
                sb.AppendLine($"  - {task.Name} (IDs: {string.Join(", ", task.Ids ?? new List<string>())})");
            }
        }
        sb.AppendLine();

        // 6. 모든 이벤트 중에서 Part 3, 4를 prerequisite로 가지는 퀘스트의 이벤트 추적
        sb.AppendLine("=== Events for quests that require HCP Part 3/4 ===");
        var questsRequiringPart3Or4 = progressService.AllTasks
            .Where(t => t.Previous?.Contains(part3Name, StringComparer.OrdinalIgnoreCase) == true ||
                        t.Previous?.Contains(part4Name, StringComparer.OrdinalIgnoreCase) == true)
            .ToList();

        var idsRequiringPart3Or4 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in questsRequiringPart3Or4)
        {
            if (task.Ids != null)
            {
                foreach (var id in task.Ids)
                    idsRequiringPart3Or4.Add(id);
            }
        }

        var eventsForRequiringQuests = events.Where(e => idsRequiringPart3Or4.Contains(e.QuestId)).OrderBy(e => e.Timestamp).ToList();
        foreach (var evt in eventsForRequiringQuests)
        {
            var task = tasksByQuestId.TryGetValue(evt.QuestId, out var t) ? t : null;
            sb.AppendLine($"  [{evt.Timestamp:MM/dd HH:mm}] {evt.EventType}: {task?.Name ?? evt.QuestId}");
        }
        sb.AppendLine();

        // 7. GetAllPrerequisites 분석
        sb.AppendLine("=== Prerequisites Analysis ===");

        // Part 3, 4를 require하는 퀘스트의 전체 prerequisites 확인
        foreach (var task in questsRequiringPart3Or4.Take(5))
        {
            sb.AppendLine($"All prerequisites for '{task.Name}':");
            var prereqs = graphService.GetAllPrerequisites(task.NormalizedName ?? "");
            foreach (var prereq in prereqs)
            {
                sb.AppendLine($"    - {prereq.Name}");
            }
        }
        sb.AppendLine();

        // 8. 시뮬레이션: 로그 이벤트로 SyncFromLogsAsync 로직 추적
        sb.AppendLine("=== Sync Logic Simulation ===");

        // Build questFinalStates
        var questFinalStates = new Dictionary<string, (QuestEventType EventType, DateTime Timestamp, TarkovTask Task)>(StringComparer.OrdinalIgnoreCase);
        var startedQuests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var evt in events)
        {
            if (!tasksByQuestId.TryGetValue(evt.QuestId, out var task))
                continue;

            var normalizedName = task.NormalizedName ?? "";
            if (string.IsNullOrEmpty(normalizedName)) continue;

            if (evt.EventType == QuestEventType.Started)
            {
                startedQuests.Add(normalizedName);
            }

            questFinalStates[normalizedName] = (evt.EventType, evt.Timestamp, task);
        }

        sb.AppendLine($"Final states count: {questFinalStates.Count}");
        sb.AppendLine($"Started quests count: {startedQuests.Count}");
        sb.AppendLine();

        // HCP 퀘스트들의 최종 상태 확인
        sb.AppendLine("HCP Quest Final States:");
        foreach (var hcpName in hcpQuests)
        {
            if (questFinalStates.TryGetValue(hcpName, out var state))
            {
                sb.AppendLine($"  {state.Task.Name}: {state.EventType} at {state.Timestamp:MM/dd HH:mm}");
            }
            else
            {
                sb.AppendLine($"  {hcpName}: NO EVENT FOUND");
            }
        }
        sb.AppendLine();

        // 9. Prerequisite auto-complete 추적
        sb.AppendLine("=== Prerequisite Auto-Complete Tracking ===");

        var terminalStateQuests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in questFinalStates)
        {
            if (kvp.Value.EventType == QuestEventType.Completed || kvp.Value.EventType == QuestEventType.Failed)
            {
                terminalStateQuests.Add(kvp.Key);
            }
        }

        sb.AppendLine($"Terminal state quests: {terminalStateQuests.Count}");
        sb.AppendLine();

        // 어떤 퀘스트가 HCP Part 3 또는 Part 4를 prerequisite로 auto-complete 하게 만드는지 찾기
        sb.AppendLine("Checking which quest triggers HCP Part 3/4 auto-complete:");
        foreach (var kvp in questFinalStates)
        {
            var normalizedName = kvp.Key;
            var (eventType, timestamp, task) = kvp.Value;

            var prereqs = graphService.GetAllPrerequisites(normalizedName);
            var part3InPrereqs = prereqs.Any(p => p.NormalizedName?.Equals(part3Name, StringComparison.OrdinalIgnoreCase) == true);
            var part4InPrereqs = prereqs.Any(p => p.NormalizedName?.Equals(part4Name, StringComparison.OrdinalIgnoreCase) == true);

            if (part3InPrereqs || part4InPrereqs)
            {
                sb.AppendLine($"  '{task.Name}' ({eventType}) has HCP in prerequisites:");
                if (part3InPrereqs) sb.AppendLine($"    - Part 3: YES");
                if (part4InPrereqs) sb.AppendLine($"    - Part 4: YES");

                // 이 퀘스트의 모든 prerequisites 출력
                sb.AppendLine($"    All prerequisites:");
                foreach (var prereq in prereqs)
                {
                    var inTerminal = terminalStateQuests.Contains(prereq.NormalizedName ?? "");
                    var inStarted = startedQuests.Contains(prereq.NormalizedName ?? "");
                    sb.AppendLine($"      - {prereq.Name} (terminal: {inTerminal}, started: {inStarted})");
                }
            }
        }

        // 결과 저장
        File.WriteAllText(outputPath, sb.ToString());
        MessageBox.Show($"Debug output saved to:\n{outputPath}", "Debug Complete", MessageBoxButton.OK, MessageBoxImage.Information);

        // 클립보드에도 복사
        Clipboard.SetText(sb.ToString());
    }
}
