# Quest List Screen PRD (퀘스트 목록 화면)

## Overview
타르코프 퀘스트 목록을 조회하고 관리할 수 있는 화면 구현

## Features

### 1. Quest Filtering (퀘스트 필터링)

#### 1.1 Kappa Quest Filter
- Required Kappa 퀘스트만 필터링하는 토글 제공
- Kappa 컨테이너 획득에 필요한 퀘스트만 표시

#### 1.2 Trader Filter
- 트레이더별 퀘스트 필터링
- 다중 선택 가능 (체크박스 또는 드롭다운)
- 트레이더 목록: Prapor, Therapist, Fence, Skier, Peacekeeper, Mechanic, Ragman, Jaeger, Lightkeeper, BTR Driver, Ref

#### 1.3 Item Required Filter
- 아이템이 필요한 퀘스트만 조회하는 필터
- FIR(Found in Raid) 아이템 필요 퀘스트 필터 옵션

### 2. Quest Status (퀘스트 상태)

#### 2.1 Status Types
- **Cannot Active**: 선행 조건 미충족으로 활성화 불가
- **Active**: 현재 진행 가능한 퀘스트
- **Done**: 완료된 퀘스트
- **Failed**: 실패한 퀘스트

#### 2.2 Default View
- 기본적으로 Active 상태의 퀘스트만 표시
- 상태별 필터 토글로 다른 상태 퀘스트 조회 가능

### 3. Quest List View (퀘스트 목록 뷰)

#### 3.1 List Layout
- 좌측: 퀘스트 목록 (스크롤 가능)
- 우측: 선택된 퀘스트 상세 정보 패널

#### 3.2 List Item Display
- 트레이더 아이콘
- 퀘스트명 (Locale 설정에 따름)
- 퀘스트 상태 배지
- 완료 버튼 (우측)

#### 3.3 Manual Completion
- 각 퀘스트 항목 우측에 완료 버튼 제공
- 완료 클릭 시:
  - 해당 퀘스트 Done 상태로 변경
  - 선행 퀘스트가 미완료 상태인 경우 자동으로 Done 처리 (재귀적)

### 4. Quest Detail Panel (퀘스트 상세 패널)

#### 4.1 Prerequisite Quest Line
- 선행 퀘스트 체인 시각화
- 트리 또는 리스트 형태로 표시
- 각 선행 퀘스트의 완료 상태 표시

#### 4.2 Prerequisites (선행 조건)
- 레벨 요구사항
- 트레이더 레벨 요구사항
- 선행 퀘스트 목록

#### 4.3 Required Items (필요 아이템)
- 아이템 이름 및 수량
- FIR(Found in Raid) 여부 표시 (체크마크 또는 아이콘)
- 아이템 이미지 (가능한 경우)

#### 4.4 Wiki Link
- 위키 바로가기 버튼
- 클릭 시 해당 퀘스트의 위키 페이지 새 창으로 열기

### 5. Localization (언어 설정)

#### 5.1 English (영어)
- 영어 퀘스트명만 표시
- 예: "Debut"

#### 5.2 Korean/Japanese (한국어/일본어)
- 메인 텍스트: 한국어/일본어 퀘스트명
  - 해당 언어 번역이 없는 경우 영어로 표시
- 서브 텍스트: 영어 퀘스트명 (작은 글씨)
- 예:
  ```
  데뷔
  Debut
  ```

### 6. Search (검색 기능)

#### 6.1 Multi-language Search
- 영어, 한국어, 일본어로 검색 가능
- 현재 Locale 설정과 무관하게 모든 언어로 검색 지원
- 실시간 검색 (타이핑 시 즉시 필터링)

### 7. Data Persistence (데이터 저장)

#### 7.1 Quest Progress
- 퀘스트 완료/실패 상태 로컬 저장
- 앱 재시작 시 상태 유지

#### 7.2 Filter Settings
- 마지막 사용한 필터 설정 저장 (선택사항)

## UI Layout

```
+------------------------------------------------------------------+
| [Search Bar]  [Kappa] [Trader▼] [Item Req] [Status▼]            |
+------------------------------------------------------------------+
|                                |                                  |
|  Quest List                    |  Quest Detail Panel              |
|  +--------------------------+  |  +----------------------------+  |
|  | [Icon] Quest Name    [✓] |  |  | Quest Name                 |  |
|  | [Icon] Quest Name    [✓] |  |  | (English subtitle)         |  |
|  | [Icon] Quest Name    [✓] |  |  +----------------------------+  |
|  | [Icon] Quest Name    [✓] |  |  | Prerequisites              |  |
|  | [Icon] Quest Name    [✓] |  |  | - Quest A (Done)           |  |
|  | ...                      |  |  | - Quest B (Active)         |  |
|  +--------------------------+  |  +----------------------------+  |
|                                |  | Required Items             |  |
|                                |  | - Item 1 x3 [FIR]          |  |
|                                |  | - Item 2 x1                |  |
|                                |  +----------------------------+  |
|                                |  | [Wiki] [Other Actions]     |  |
|                                |  +----------------------------+  |
+------------------------------------------------------------------+
```

## Technical Considerations

### Data Source
- tarkov.dev API를 통한 퀘스트 데이터 조회
- 기존 TarkovDataService 활용

### State Management
- 퀘스트 완료 상태 로컬 파일 또는 설정에 저장
- JSON 형태로 퀘스트 ID와 상태 매핑

### Performance
- 퀘스트 데이터 캐싱
- 필터링은 클라이언트 사이드에서 처리

## Dependencies
- Existing: TarkovDataService, TarkovTask model
- New: QuestProgressService (퀘스트 진행 상태 관리)
