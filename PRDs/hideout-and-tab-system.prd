# Hideout & Tab System PRD (은신처 및 탭 시스템)

## Overview
기존 퀘스트 페이지를 탭 기반 시스템으로 전환하고, Hideout(은신처) 및 Items(아이템) 탭을 추가하여 통합 진행 관리 시스템 구현

## Reference
- Wiki: https://escapefromtarkov.fandom.com/wiki/Hideout
- API: tarkov.dev GraphQL API (Locale 정보)

## Features

### 1. Tab Navigation System (탭 네비게이션)

#### 1.1 Tab Structure
- **Quests 탭**: 기존 퀘스트 목록 화면 (현재 구현된 기능)
- **Hideout 탭**: 은신처 모듈 관리
- **Items 탭**: 필요 아이템 총합 조회

#### 1.2 Tab UI
```
+------------------------------------------------------------------+
| [Quests]  [Hideout]  [Items]                                      |
+------------------------------------------------------------------+
|                         Tab Content Area                          |
+------------------------------------------------------------------+
```

### 2. Hideout Tab (은신처 탭)

#### 2.1 Hideout Module List
좌측 패널에 은신처 모듈 목록 표시

**모듈 목록 (총 27개):**
- Air Filtering Unit (공기 정화 장치) - Lv.1
- Bitcoin Farm (비트코인 농장) - Lv.1-3
- Booze Generator (술 제조기) - Lv.1
- Cultist Circle (컬티스트 서클) - Lv.1
- Defective Wall (결함 있는 벽) - Lv.1-6
- Gear Rack (장비 거치대) - Lv.1-3
- Generator (발전기) - Lv.1-3
- Gym (체육관) - Lv.1
- Hall of Fame (명예의 전당) - Lv.1-3
- Heating (난방) - Lv.1-3
- Illumination (조명) - Lv.1-3
- Intelligence Center (정보 센터) - Lv.1-3
- Lavatory (화장실) - Lv.1-3
- Library (도서관) - Lv.1
- Medstation (의료 시설) - Lv.1-3
- Nutrition Unit (영양 장치) - Lv.1-3
- Rest Space (휴식 공간) - Lv.1-3
- Scav Case (스캐브 케이스) - Lv.1-3
- Security (보안) - Lv.1-3
- Shooting Range (사격장) - Lv.1-2
- Solar Power (태양열 발전) - Lv.1-3
- Stash (보관함) - Lv.1-4
- Vents (환기구) - Lv.1-3
- Water Collector (물 수집기) - Lv.1-3
- Weapon Rack (무기 거치대) - Lv.1-2
- Workbench (작업대) - Lv.1-3
- Christmas Tree (크리스마스 트리) - Seasonal

#### 2.2 List Item Display
```
+--------------------------------------------------+
| [Icon] Module Name              Lv.X  [-] [+]    |
+--------------------------------------------------+
```

- **좌측 아이콘**: 해당 은신처 모듈의 아이콘 이미지
  - Source: Wiki 이미지 또는 tarkov.dev API
  - 캐시하여 오프라인에서도 표시
- **모듈명**: Locale 설정에 따른 언어로 표시
- **현재 레벨**: 현재 완료된 레벨 표시 (0 = 미건설)
- **[-] 버튼**: 레벨 감소 (최소 0)
- **[+] 버튼**: 레벨 증가 (최대 레벨까지)

#### 2.3 Level Control Logic
```
[-] 클릭 시:
  - currentLevel > 0 이면 currentLevel -= 1
  - currentLevel == 0 이면 비활성화

[+] 클릭 시:
  - currentLevel < maxLevel 이면 currentLevel += 1
  - currentLevel == maxLevel 이면 비활성화
```

#### 2.4 Visual States
- **완료 레벨**: 회색 배경 또는 체크 표시
- **현재 목표 레벨**: 하이라이트 (다음 건설 대상)
- **미완료 레벨**: 기본 표시

### 3. Hideout Detail Panel (은신처 상세 패널)

#### 3.1 Panel Layout
```
+-------------------------------------------+
| [Icon] Module Name                        |
| (English subtitle if non-EN locale)       |
+-------------------------------------------+
| Current Level: X / Max Level: Y           |
+-------------------------------------------+
| Next Level Requirements (Lv.X+1)          |
| +---------------------------------------+ |
| | Items:                                | |
| | - Item A x5 [FIR]                     | |
| | - Item B x3                           | |
| | Traders:                              | |
| | - Mechanic Lv.2                       | |
| | Skills:                               | |
| | - Strength Lv.3                       | |
| | Other Modules:                        | |
| | - Generator Lv.2                      | |
| +---------------------------------------+ |
+-------------------------------------------+
| Total Remaining Requirements              |
| (현재 레벨 이후 모든 레벨 합산)            |
| +---------------------------------------+ |
| | - Item A x15 (5 + 5 + 5)              | |
| | - Item B x10 (3 + 3 + 4)              | |
| | - Item C x2                           | |
| +---------------------------------------+ |
+-------------------------------------------+
| [Wiki]                                    |
+-------------------------------------------+
```

#### 3.2 Next Level Requirements
- 다음 레벨 업그레이드에 필요한 요소 표시
- **필요 아이템**: 아이템명, 수량, FIR(Found in Raid) 여부
- **트레이더 레벨**: 필요한 트레이더 레벨
- **스킬 요구사항**: 필요한 캐릭터 스킬 및 레벨
- **선행 모듈**: 필요한 다른 은신처 모듈 및 레벨

#### 3.3 Total Remaining Requirements
- 완료된 레벨 제외, 남은 모든 레벨에 필요한 아이템 총합
- 동일 아이템은 합산하여 표시
- 예: Lv.2, Lv.3 각각 Item A x5 필요시 → Item A x10 표시

#### 3.4 Wiki Link
- 해당 은신처 모듈의 위키 페이지로 바로가기

### 4. Items Tab (아이템 탭)

#### 4.1 Purpose
퀘스트 + 은신처에서 필요한 아이템 총합 조회

#### 4.2 Item List Display
```
+------------------------------------------------------------------+
| [Search]  [Filter: All / Quest / Hideout]  [FIR Only]            |
+------------------------------------------------------------------+
| [Img] Item Name                    Quest: X  Hideout: Y  Total: Z |
| [Img] Item Name                    Quest: X  Hideout: Y  Total: Z |
| [Img] Item Name [FIR]              Quest: X  Hideout: Y  Total: Z |
+------------------------------------------------------------------+
```

#### 4.3 Item Information
- **아이템 이미지**: 썸네일
- **아이템명**: Locale 설정에 따른 언어
- **FIR 표시**: Found in Raid 필요시 뱃지 표시
- **필요 수량**:
  - Quest: 퀘스트에서 필요한 수량 (미완료 퀘스트 기준)
  - Hideout: 은신처에서 필요한 수량 (미완료 레벨 기준)
  - Total: 총 필요 수량

#### 4.4 Filtering
- **All**: 모든 필요 아이템
- **Quest**: 퀘스트 필요 아이템만
- **Hideout**: 은신처 필요 아이템만
- **FIR Only**: FIR 필요 아이템만

### 5. Data Sources (데이터 소스)

#### 5.1 Wiki Parsing
- **Source**: https://escapefromtarkov.fandom.com/wiki/Hideout
- **Data**:
  - 각 모듈별 레벨 정보
  - 레벨별 필요 아이템 및 수량
  - FIR 여부
  - 선행 조건 (트레이더, 스킬, 다른 모듈)
  - 모듈 아이콘 이미지 URL

#### 5.2 Tarkov.dev API
- **Locale 정보**:
  - 모듈명 다국어 지원 (EN, KO, JA 등)
  - 아이템명 다국어 지원
- **GraphQL Query 예시**:
```graphql
query {
  hideoutStations(lang: ko) {
    id
    name
    normalizedName
    imageLink
    levels {
      level
      itemRequirements {
        item {
          id
          name
          shortName
          iconLink
        }
        count
      }
      stationLevelRequirements {
        station {
          id
          name
        }
        level
      }
      traderRequirements {
        trader {
          id
          name
        }
        level
      }
      skillRequirements {
        name
        level
      }
    }
  }
}
```

### 6. Data Persistence (데이터 저장)

#### 6.1 Hideout Progress
- JSON 형태로 로컬 저장
- 파일: `hideout_progress.json`
```json
{
  "version": 1,
  "lastUpdated": "2024-01-01T00:00:00Z",
  "modules": {
    "generator": 2,
    "workbench": 1,
    "medstation": 0,
    ...
  }
}
```

#### 6.2 Auto-save
- 레벨 변경 시 자동 저장
- 앱 재시작 시 진행 상태 복원

### 7. Localization (언어 지원)

#### 7.1 Supported Languages
- English (EN)
- Korean (KO)
- Japanese (JA)

#### 7.2 Display Format
- **EN**: 영어 이름만 표시
- **KO/JA**:
  - 메인: 해당 언어 이름
  - 서브: 영어 이름 (작은 글씨)

### 8. Image Caching (이미지 캐싱)

#### 8.1 Cache Strategy
- 은신처 모듈 아이콘 로컬 캐시
- 아이템 이미지 로컬 캐시
- 캐시 만료: 7일 또는 수동 새로고침

#### 8.2 Cache Location
- `AppData/TarkovHelper/ImageCache/`

## UI Layout

### Hideout Tab Layout
```
+------------------------------------------------------------------+
| [Quests]  [Hideout]  [Items]                                      |
+------------------------------------------------------------------+
| [Search]                                                          |
+------------------------------------------------------------------+
|                                |                                  |
|  Module List                   |  Module Detail Panel             |
|  +--------------------------+  |  +----------------------------+  |
|  | [Icon] Generator   Lv.2  |  |  | [Icon] Generator           |  |
|  |              [-] [+]     |  |  | (발전기)                   |  |
|  | [Icon] Workbench  Lv.1   |  |  +----------------------------+  |
|  |              [-] [+]     |  |  | Current: Lv.2 / Max: Lv.3  |  |
|  | [Icon] Medstation Lv.0   |  |  +----------------------------+  |
|  |              [-] [+]     |  |  | Next Level (Lv.3)          |  |
|  | [Icon] Heating    Lv.3   |  |  | - Fuel Tank x2             |  |
|  |              [-] [+]     |  |  | - Cable x5 [FIR]           |  |
|  | ...                      |  |  | - Mechanic Lv.3            |  |
|  +--------------------------+  |  +----------------------------+  |
|                                |  | Total Remaining            |  |
|                                |  | - Fuel Tank x2             |  |
|                                |  | - Cable x5                 |  |
|                                |  +----------------------------+  |
|                                |  | [Wiki]                     |  |
|                                |  +----------------------------+  |
+------------------------------------------------------------------+
```

## Technical Considerations

### Services
- `HideoutDataService`: 은신처 데이터 조회 및 관리
- `HideoutProgressService`: 은신처 진행 상태 관리
- `WikiHideoutParser`: 위키에서 은신처 데이터 파싱
- `ImageCacheService`: 이미지 캐시 관리 (기존 확장)

### Models
```csharp
public class HideoutModule
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string NormalizedName { get; set; }
    public string ImageUrl { get; set; }
    public int MaxLevel { get; set; }
    public List<HideoutLevel> Levels { get; set; }
}

public class HideoutLevel
{
    public int Level { get; set; }
    public List<ItemRequirement> ItemRequirements { get; set; }
    public List<ModuleRequirement> ModuleRequirements { get; set; }
    public List<TraderRequirement> TraderRequirements { get; set; }
    public List<SkillRequirement> SkillRequirements { get; set; }
}

public class ItemRequirement
{
    public string ItemId { get; set; }
    public string ItemName { get; set; }
    public string IconUrl { get; set; }
    public int Count { get; set; }
    public bool FoundInRaid { get; set; }
}
```

### Dependencies
- Existing: TarkovDataService, ImageCacheService
- New: HideoutDataService, HideoutProgressService, WikiHideoutParser

## Implementation Priority

1. **Phase 1**: Tab 시스템 구현 (Quests를 첫 번째 탭으로)
2. **Phase 2**: Hideout 탭 기본 구조 및 데이터 로딩
3. **Phase 3**: Hideout 상세 패널 및 레벨 조절 기능
4. **Phase 4**: Items 탭 구현
5. **Phase 5**: 진행 상태 저장 및 복원
6. **Phase 6**: 이미지 캐싱 최적화
