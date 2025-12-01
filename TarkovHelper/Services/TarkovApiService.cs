using System.Net.Http;
using System.Text;
using System.Text.Json;
using TarkovHelper.Models;
using TarkovHelper.Models.GraphQL;

namespace TarkovHelper.Services;

/// <summary>
/// Tarkov.dev GraphQL API 클라이언트
/// </summary>
public class TarkovApiService : IDisposable
{
    private readonly HttpClient _httpClient;
    private const string ApiEndpoint = "https://api.tarkov.dev/graphql";

    private const string TasksQuery = """
        query GetTasks($lang: LanguageCode!) {
            tasks(lang: $lang) {
                id
                name
                normalizedName
                trader { name }
                minPlayerLevel
                experience
                kappaRequired
                lightkeeperRequired
                wikiLink
                taskRequirements {
                    task { id name }
                    status
                }
                objectives {
                    id
                    type
                    description
                    optional
                    maps { name normalizedName }
                    ... on TaskObjectiveItem {
                        items { id name shortName }
                        count
                        foundInRaid
                    }
                }
            }
        }
        """;

    private const string ItemsQuery = """
        query GetItems($lang: LanguageCode!) {
            items(lang: $lang) {
                id
                name
                shortName
                normalizedName
                wikiLink
                iconLink
                gridImageLink
                basePrice
                width
                height
                types
                category { name }
            }
        }
        """;

    private const string HideoutStationsQuery = """
        query GetHideoutStations($lang: LanguageCode!) {
            hideoutStations(lang: $lang) {
                id
                name
                normalizedName
                imageLink
                levels {
                    id
                    level
                    constructionTime
                    description
                    itemRequirements {
                        id
                        item { id name shortName }
                        count
                        quantity
                        attributes { type name value }
                    }
                    stationLevelRequirements {
                        id
                        station { id name }
                        level
                    }
                    traderRequirements {
                        id
                        trader { name }
                        level
                        value
                    }
                    skillRequirements {
                        id
                        name
                        level
                    }
                }
            }
        }
        """;

    public TarkovApiService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    /// <summary>
    /// 특정 언어로 모든 Task 데이터를 가져옵니다
    /// </summary>
    public async Task<List<ApiTask>> GetTasksAsync(string langCode)
    {
        var request = new
        {
            query = TasksQuery,
            variables = new { lang = langCode }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(ApiEndpoint, content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GraphQLResponse<TasksQueryResponse>>(responseJson);

        if (result?.Errors?.Count > 0)
        {
            throw new Exception($"GraphQL Error: {string.Join(", ", result.Errors.Select(e => e.Message))}");
        }

        return result?.Data?.Tasks ?? [];
    }

    /// <summary>
    /// 특정 언어로 모든 아이템 데이터를 가져옵니다
    /// </summary>
    public async Task<List<ApiItem>> GetItemsAsync(string langCode)
    {
        var request = new
        {
            query = ItemsQuery,
            variables = new { lang = langCode }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(ApiEndpoint, content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GraphQLResponse<ItemsQueryResponse>>(responseJson);

        if (result?.Errors?.Count > 0)
        {
            throw new Exception($"GraphQL Error: {string.Join(", ", result.Errors.Select(e => e.Message))}");
        }

        return result?.Data?.Items ?? [];
    }

    /// <summary>
    /// 영어/한글/일본어 데이터를 병합하여 TaskDataset을 생성합니다
    /// </summary>
    public async Task<TaskDataset> BuildTaskDatasetAsync()
    {
        // 영어, 한글, 일본어 데이터 동시에 가져오기
        var enTask = GetTasksAsync("en");
        var koTask = GetTasksAsync("ko");
        var jaTask = GetTasksAsync("ja");

        await Task.WhenAll(enTask, koTask, jaTask);

        var enTasks = enTask.Result;
        var koTasks = koTask.Result;
        var jaTasks = jaTask.Result;

        // 한글/일본어 데이터 매핑 (ID -> 해당 언어 Task)
        var koTaskMap = koTasks.ToDictionary(t => t.Id);
        var jaTaskMap = jaTasks.ToDictionary(t => t.Id);

        // 중복 제거: 같은 이름의 퀘스트가 여러 개 있으면 ID가 나중인 것(더 큰 값)만 유지
        // 중복된 ID를 새 ID로 매핑하는 딕셔너리 생성
        // AlternativeIds: 중복 퀘스트의 다른 ID들을 저장 (로그 감지 시 활용)
        var idRemapping = new Dictionary<string, string>();
        var alternativeIdsMap = new Dictionary<string, List<string>>();
        var deduplicatedTasks = enTasks
            .GroupBy(t => t.Name)
            .SelectMany(g =>
            {
                if (g.Count() == 1) return g;

                // ID 문자열 비교로 가장 나중 ID 선택
                var sortedTasks = g.OrderByDescending(t => t.Id, StringComparer.Ordinal).ToList();
                var keepTask = sortedTasks.First();

                // 제거될 ID들을 유지할 ID로 매핑하고, AlternativeIds에 저장
                var altIds = new List<string>();
                foreach (var removedTask in sortedTasks.Skip(1))
                {
                    idRemapping[removedTask.Id] = keepTask.Id;
                    altIds.Add(removedTask.Id);
                }
                alternativeIdsMap[keepTask.Id] = altIds;

                return [keepTask];
            })
            .ToList();

        // 선행 퀘스트 -> 후행 퀘스트 매핑 구축 (중복 제거된 목록 사용)
        var followUpMap = new Dictionary<string, List<string>>();
        foreach (var task in deduplicatedTasks)
        {
            foreach (var req in task.TaskRequirements)
            {
                if (req.Task == null) continue;

                // 리매핑된 ID 사용
                var prereqId = idRemapping.GetValueOrDefault(req.Task.Id, req.Task.Id);
                if (!followUpMap.ContainsKey(prereqId))
                {
                    followUpMap[prereqId] = [];
                }
                followUpMap[prereqId].Add(task.Id);
            }
        }

        // TaskData 목록 생성
        var tasks = deduplicatedTasks.Select(t => new TaskData
        {
            Id = t.Id,
            NameEn = t.Name,
            NameKo = koTaskMap.GetValueOrDefault(t.Id)?.Name ?? t.Name,
            NameJa = jaTaskMap.GetValueOrDefault(t.Id)?.Name ?? t.Name,
            NormalizedName = t.NormalizedName,
            TraderName = t.Trader?.Name ?? "Unknown",
            MinPlayerLevel = t.MinPlayerLevel,
            Experience = t.Experience,
            KappaRequired = t.KappaRequired ?? false,
            LightkeeperRequired = t.LightkeeperRequired ?? false,
            WikiLink = t.WikiLink,
            PrerequisiteTaskIds = t.TaskRequirements
                .Where(r => r.Task != null)
                .Select(r => idRemapping.GetValueOrDefault(r.Task!.Id, r.Task!.Id)) // 리매핑된 ID 사용
                .Distinct() // 중복 제거
                .ToList(),
            FollowUpTaskIds = followUpMap.GetValueOrDefault(t.Id, []),
            AlternativeIds = alternativeIdsMap.GetValueOrDefault(t.Id, []),
            Objectives = t.Objectives.Select(o => new TaskObjective
            {
                Id = o.Id,
                Type = o.Type,
                Description = o.Description,
                Optional = o.Optional,
                Maps = o.Maps?.Select(m => m.NormalizedName).ToList() ?? [],
                Items = o.Items?.Select(i => new ObjectiveItem
                {
                    ItemId = i.Id,
                    Count = o.Count ?? 1,
                    FoundInRaid = o.FoundInRaid ?? false
                }).ToList() ?? []
            }).ToList()
        }).ToList();

        return new TaskDataset
        {
            Version = "1.0",
            GeneratedAt = DateTime.UtcNow,
            Tasks = tasks
        };
    }

    /// <summary>
    /// 영어/한글/일본어 데이터를 병합하여 ItemDataset을 생성합니다
    /// </summary>
    public async Task<ItemDataset> BuildItemDatasetAsync()
    {
        // 영어, 한글, 일본어 데이터 동시에 가져오기
        var enTask = GetItemsAsync("en");
        var koTask = GetItemsAsync("ko");
        var jaTask = GetItemsAsync("ja");

        await Task.WhenAll(enTask, koTask, jaTask);

        var enItems = enTask.Result;
        var koItems = koTask.Result;
        var jaItems = jaTask.Result;

        // 한글/일본어 데이터 매핑 (ID -> 해당 언어 Item)
        var koItemMap = koItems.ToDictionary(i => i.Id);
        var jaItemMap = jaItems.ToDictionary(i => i.Id);

        // ItemData 목록 생성
        var items = enItems.Select(i => new ItemData
        {
            Id = i.Id,
            NameEn = i.Name ?? "",
            NameKo = koItemMap.GetValueOrDefault(i.Id)?.Name ?? i.Name ?? "",
            NameJa = jaItemMap.GetValueOrDefault(i.Id)?.Name ?? i.Name ?? "",
            ShortNameEn = i.ShortName ?? "",
            ShortNameKo = koItemMap.GetValueOrDefault(i.Id)?.ShortName ?? i.ShortName ?? "",
            ShortNameJa = jaItemMap.GetValueOrDefault(i.Id)?.ShortName ?? i.ShortName ?? "",
            NormalizedName = i.NormalizedName ?? "",
            WikiLink = i.WikiLink,
            IconLink = i.IconLink,
            GridImageLink = i.GridImageLink,
            BasePrice = i.BasePrice,
            Width = i.Width,
            Height = i.Height,
            Types = i.Types,
            CategoryName = i.Category?.Name
        }).ToList();

        return new ItemDataset
        {
            Version = "1.0",
            GeneratedAt = DateTime.UtcNow,
            Items = items
        };
    }

    /// <summary>
    /// 특정 언어로 모든 Hideout 스테이션 데이터를 가져옵니다
    /// </summary>
    public async Task<List<ApiHideoutStation>> GetHideoutStationsAsync(string langCode)
    {
        var request = new
        {
            query = HideoutStationsQuery,
            variables = new { lang = langCode }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(ApiEndpoint, content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GraphQLResponse<HideoutStationsQueryResponse>>(responseJson);

        if (result?.Errors?.Count > 0)
        {
            throw new Exception($"GraphQL Error: {string.Join(", ", result.Errors.Select(e => e.Message))}");
        }

        return result?.Data?.HideoutStations ?? [];
    }

    /// <summary>
    /// 영어/한글/일본어 데이터를 병합하여 HideoutDataset을 생성합니다
    /// </summary>
    public async Task<HideoutDataset> BuildHideoutDatasetAsync()
    {
        // 영어, 한글, 일본어 데이터 동시에 가져오기
        var enTask = GetHideoutStationsAsync("en");
        var koTask = GetHideoutStationsAsync("ko");
        var jaTask = GetHideoutStationsAsync("ja");

        await Task.WhenAll(enTask, koTask, jaTask);

        var enStations = enTask.Result;
        var koStations = koTask.Result;
        var jaStations = jaTask.Result;

        // 한글/일본어 데이터 매핑 (ID -> 해당 언어 Station)
        var koStationMap = koStations.ToDictionary(s => s.Id);
        var jaStationMap = jaStations.ToDictionary(s => s.Id);

        // 한글/일본어 레벨 데이터 매핑 (ID -> 해당 언어 Level)
        var koLevelMap = koStations
            .SelectMany(s => s.Levels)
            .ToDictionary(l => l.Id);
        var jaLevelMap = jaStations
            .SelectMany(s => s.Levels)
            .ToDictionary(l => l.Id);

        // HideoutData 목록 생성
        var hideouts = enStations.Select(s =>
        {
            var koStation = koStationMap.GetValueOrDefault(s.Id);
            var jaStation = jaStationMap.GetValueOrDefault(s.Id);

            return new HideoutData
            {
                Id = s.Id,
                NameEn = s.Name,
                NameKo = koStation?.Name ?? s.Name,
                NameJa = jaStation?.Name ?? s.Name,
                NormalizedName = s.NormalizedName,
                ImageLink = s.ImageLink,
                Levels = s.Levels.Select(l =>
                {
                    var koLevel = koLevelMap.GetValueOrDefault(l.Id);
                    var jaLevel = jaLevelMap.GetValueOrDefault(l.Id);

                    return new HideoutLevel
                    {
                        Id = l.Id,
                        Level = l.Level,
                        ConstructionTime = l.ConstructionTime,
                        DescriptionEn = l.Description,
                        DescriptionKo = koLevel?.Description ?? l.Description,
                        DescriptionJa = jaLevel?.Description ?? l.Description,
                        ItemRequirements = l.ItemRequirements.Select(r => new HideoutItemRequirement
                        {
                            ItemId = r.Item.Id,
                            ItemNameEn = r.Item.Name,
                            ItemNameKo = r.Item.Name, // 아이템 이름은 별도 조회 필요
                            ItemNameJa = r.Item.Name, // 아이템 이름은 별도 조회 필요
                            Count = r.Count > 0 ? r.Count : r.Quantity,
                            // foundInRaid 속성이 있고 value가 "true"인 경우에만 FIR 요구
                            FoundInRaid = r.Attributes?.Any(a =>
                                (a.Name.Equals("foundInRaid", StringComparison.OrdinalIgnoreCase) ||
                                 a.Type.Equals("foundInRaid", StringComparison.OrdinalIgnoreCase)) &&
                                (a.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false)) ?? false
                        }).ToList(),
                        StationLevelRequirements = l.StationLevelRequirements.Select(r => new HideoutStationRequirement
                        {
                            StationId = r.Station.Id,
                            StationNameEn = r.Station.Name,
                            StationNameKo = koStationMap.GetValueOrDefault(r.Station.Id)?.Name ?? r.Station.Name,
                            StationNameJa = jaStationMap.GetValueOrDefault(r.Station.Id)?.Name ?? r.Station.Name,
                            Level = r.Level
                        }).ToList(),
                        TraderRequirements = l.TraderRequirements.Select(r => new HideoutTraderRequirement
                        {
                            TraderId = "", // API에서 trader ID를 제공하지 않음
                            TraderNameEn = r.Trader.Name,
                            TraderNameKo = r.Trader.Name, // 트레이더 이름 번역은 별도 처리 필요
                            Level = r.Value ?? r.Level ?? 0
                        }).ToList(),
                        SkillRequirements = l.SkillRequirements.Select(r => new HideoutSkillRequirement
                        {
                            SkillNameEn = r.Name,
                            SkillNameKo = r.Name, // 스킬 이름 번역은 별도 처리 필요
                            Level = r.Level
                        }).ToList()
                    };
                }).ToList()
            };
        }).ToList();

        return new HideoutDataset
        {
            Version = "1.0",
            GeneratedAt = DateTime.UtcNow,
            Hideouts = hideouts
        };
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
