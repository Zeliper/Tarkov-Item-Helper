# TarkovHelper Database Update Mechanism

TarkovHelper의 데이터베이스 업데이트 메커니즘에 대한 상세 문서입니다.

---

## 개요

TarkovHelper는 **자동 DB 다운로드 기능이 없습니다**. 대신 다음과 같은 구조로 동작합니다:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        TarkovDBEditor (별도 도구)                        │
│  ┌─────────────┐   ┌──────────────┐   ┌─────────────────────────────┐  │
│  │ tarkov.dev  │ → │  Wiki 캐시   │ → │  tarkov_data.db 생성/갱신   │  │
│  │    API      │   │   저장       │   │                             │  │
│  └─────────────┘   └──────────────┘   └─────────────────────────────┘  │
└────────────────────────────────────────────────────────┬────────────────┘
                                                         │
                                                         ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                           Release Package                                │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │  TarkovHelper.zip                                                │   │
│  │  ├── TarkovHelper.exe                                           │   │
│  │  ├── Assets/                                                     │   │
│  │  │   ├── tarkov_data.db  ← 번들된 마스터 데이터                  │   │
│  │  │   └── db_version.txt                                          │   │
│  │  └── ...                                                         │   │
│  └─────────────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────┬────────────────┘
                                                         │
                                              AutoUpdater.NET
                                                         │
                                                         ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                           TarkovHelper (사용자 PC)                       │
│  ┌─────────────────────┐     ┌─────────────────────────────────────┐   │
│  │   tarkov_data.db    │     │        user_data.db                  │   │
│  │   (읽기 전용)        │     │        (사용자 진행상황)             │   │
│  │   - Items           │     │        - QuestProgress               │   │
│  │   - Quests          │     │        - HideoutProgress             │   │
│  │   - MapMarkers      │     │        - ItemInventory               │   │
│  │   - Hideout         │     │        - UserSettings                │   │
│  └─────────────────────┘     └─────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 데이터 흐름

### 1. TarkovDBEditor에서 데이터 수집

TarkovDBEditor는 다음 소스에서 데이터를 수집합니다:

#### tarkov.dev GraphQL API
```graphql
# 아이템 데이터
query Items($lang: LanguageCode!) {
  items(lang: $lang) {
    id, name, normalizedName, shortName
    iconLink, wikiLink, category { ... }
  }
}

# 퀘스트 데이터
query Tasks($lang: LanguageCode!) {
  tasks(lang: $lang) {
    id, name, normalizedName, trader { ... }
    objectives { ... }, requirements { ... }
  }
}

# 하이드아웃 데이터
query HideoutStations($lang: LanguageCode!) {
  hideoutStations(lang: $lang) {
    id, name, levels { ... }
  }
}
```

#### Wiki 데이터 캐싱
- `TarkovDBEditor/Services/WikiCacheService.cs`: Wiki 페이지 캐싱
- `TarkovDBEditor/Services/WikiQuestService.cs`: Wiki 퀘스트 파싱
- 캐시 위치: `TarkovDBEditor/wiki_data/`

### 2. DB 파일 생성/업데이트

**핵심 서비스**: `TarkovDBEditor/Services/RefreshDataService.cs`

```csharp
public async Task<RefreshResult> RefreshDataFromCacheAsync(
    string databasePath,
    TarkovDevDataService? tarkovDevService = null,
    WikiCacheService? wikiCacheService = null,
    Action<string>? progress = null,
    CancellationToken cancellationToken = default)
{
    // 1. 기존 DB에서 Items 로드
    // 2. 캐시된 Quests 로드
    // 3. DB 업데이트 (Quests, Requirements, Objectives 등)
    // 4. Traders 업데이트
}
```

### 3. 앱 릴리즈 패키징

**TarkovHelper.csproj** 설정:
```xml
<None Update="Assets\tarkov_data.db">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

**CreateRelease.bat**:
```batch
# 빌드 출력 → TarkovHelper.zip 패키징
# tarkov_data.db가 포함됨
```

### 4. AutoUpdater.NET 앱 업데이트

**update.xml** (GitHub 호스팅):
```xml
<?xml version="1.0" encoding="UTF-8"?>
<item>
    <version>4.0.2</version>
    <url>https://github.com/Zeliper/Tarkov-Item-Helper/releases/download/v4.0.2/TarkovHelper.zip</url>
    <changelog>https://github.com/Zeliper/Tarkov-Item-Helper/releases/latest</changelog>
    <mandatory>false</mandatory>
</item>
```

**App.xaml.cs**:
```csharp
private const string UpdateXmlUrl = "https://raw.githubusercontent.com/Zeliper/Tarkov-Item-Helper/main/update.xml";

// 시작 시 업데이트 체크
AutoUpdater.Start(UpdateXmlUrl);
```

---

## 데이터베이스 구조

### Master Data (tarkov_data.db) - 읽기 전용

| 테이블 | 용도 |
|--------|------|
| `Items` | 게임 아이템 정보 |
| `Quests` | 퀘스트 기본 정보 |
| `QuestRequirements` | 퀘스트 선행 조건 |
| `QuestObjectives` | 퀘스트 목표 |
| `QuestRequiredItems` | 퀘스트 필요 아이템 |
| `HideoutStations` | 하이드아웃 스테이션 |
| `HideoutLevels` | 하이드아웃 레벨별 정보 |
| `HideoutItemRequirements` | 하이드아웃 필요 아이템 |
| `MapMarkers` | 맵 마커 (탈출구, 스폰 등) |
| `MapFloorLocations` | 맵 층 정의 |
| `Traders` | 트레이더 정보 |

### User Data (user_data.db) - 읽기/쓰기

| 테이블 | 용도 |
|--------|------|
| `QuestProgress` | 퀘스트 완료 상태 |
| `ObjectiveProgress` | 목표별 완료 상태 |
| `HideoutProgress` | 하이드아웃 건설 진행 |
| `ItemInventory` | 보유 아이템 (FIR/Non-FIR) |
| `UserSettings` | 앱 설정 |

---

## 버전 관리

### DB 버전
- 파일: `Assets/db_version.txt`
- 현재: `1.0.1`
- **참고**: 현재 이 버전은 런타임에 사용되지 않음

### 앱 버전 변경 시 처리

```csharp
// App.xaml.cs
private void CheckAndRefreshDataOnVersionChange()
{
    var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
    var savedVersion = GetSavedVersion();

    if (savedVersion != currentVersion)
    {
        // 캐시 데이터 삭제 (user_data.db는 보존)
        DeleteCacheDataFiles();
        SaveCurrentVersion(currentVersion);
    }
}
```

---

## Map 데이터 소스

### MapMarkers 테이블

v3.5.0에서 tarkov.dev API를 사용하던 것이 현재는 DB 테이블로 변경됨:

```sql
SELECT Id, Name, NameKo, MarkerType, MapKey, X, Y, Z, FloorId
FROM MapMarkers
WHERE MapKey = @MapKey
```

**MarkerType 종류**:
- `PmcSpawn` - PMC 스폰 지점
- `ScavSpawn` - Scav 스폰 지점
- `PmcExtraction` - PMC 탈출구
- `ScavExtraction` - Scav 탈출구
- `SharedExtraction` - 공용 탈출구
- `Transit` - 환승 지점
- `BossSpawn` - 보스 스폰
- `Lever` - 레버

### QuestObjectives 테이블 (위치 포함)

```sql
SELECT Id, QuestId, ObjectiveType, Description, MapName,
       LocationPoints, OptionalPoints
FROM QuestObjectives
WHERE MapName = @MapName
```

**LocationPoints JSON 형식**:
```json
[{"X": 123.5, "Y": 0, "Z": -45.2, "FloorId": "main"}]
```

### map_configs.json

맵별 좌표 변환 설정 (Assets/DB/Data/map_configs.json):

```json
{
  "maps": [
    {
      "key": "Customs",
      "displayName": "Customs",
      "svgFileName": "Customs.svg",
      "calibratedTransform": [...],
      "playerMarkerTransform": [...],
      "floors": [
        {"layerId": "main", "displayName": "Ground Floor", "order": 0}
      ]
    }
  ]
}
```

---

## 업데이트 워크플로우

### 개발자 워크플로우

```
1. TarkovDBEditor 실행
2. "Refresh Data" 버튼 클릭
   - tarkov.dev API에서 최신 데이터 가져옴
   - Wiki 데이터 캐시 업데이트
   - tarkov_data.db 업데이트
3. Map Editor에서 마커 편집 (필요시)
4. 변경된 tarkov_data.db를 TarkovHelper/Assets/에 복사
5. TarkovHelper 빌드
6. GitHub Release 생성
7. update.xml 업데이트
```

### 사용자 워크플로우

```
1. TarkovHelper 시작
2. AutoUpdater가 update.xml 체크
3. 새 버전 발견 시:
   - 다운로드 확인 대화상자 표시
   - TarkovHelper.zip 다운로드
   - 자동 설치 및 재시작
4. 새 tarkov_data.db가 자동으로 포함됨
```

---

## 관련 파일

### TarkovHelper
- `App.xaml.cs` - AutoUpdater 설정, 버전 체크
- `Services/UserDataDbService.cs` - 사용자 데이터 관리
- `Services/MigrationService.cs` - 버전 마이그레이션

### TarkovDBEditor
- `Services/RefreshDataService.cs` - 데이터 새로고침
- `Services/TarkovDevDataService.cs` - API 연동
- `Services/WikiCacheService.cs` - Wiki 캐싱
- `Services/MapMarkerService.cs` - 맵 마커 관리
- `Services/DatabaseService.cs` - DB 코어 서비스

### 설정 파일
- `update.xml` - AutoUpdater 설정
- `Assets/db_version.txt` - DB 버전
- `Assets/DB/Data/map_configs.json` - 맵 설정

---

## 향후 개선 가능성

1. **별도 DB 업데이트**: 앱 업데이트 없이 DB만 업데이트하는 기능
2. **DB 버전 체크**: db_version.txt를 활용한 DB 버전 확인
3. **증분 업데이트**: 변경된 데이터만 다운로드
4. **백그라운드 동기화**: 앱 실행 중 자동 데이터 동기화
