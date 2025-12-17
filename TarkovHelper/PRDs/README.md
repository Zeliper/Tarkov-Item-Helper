# PRDs (Product Requirements Documents)

이 폴더는 TarkovHelper 프로젝트의 기능 개발 계획을 관리합니다.

## Folder Structure

```
PRDs/
├── README.md              # 이 파일
├── active/                # 진행 중인 PRD
├── archive/               # 완료된 PRD (월별 정리)
└── templates/             # PRD 템플릿
```

## Workflow

### 1. 새 기능 계획
1. `templates/feature-template.prd`를 복사하여 `active/` 폴더에 생성
2. 파일명: `feature-[기능명].prd` (예: `feature-map-v2.prd`)
3. PRD 내용 작성

### 2. 작업 진행
1. Status를 "In Progress"로 변경
2. 각 Task 완료 시 체크박스 체크
3. Progress Log에 진행 상황 기록
4. 관련 에이전트의 Learning Log 업데이트 요청

### 3. 완료 및 아카이빙
1. 모든 Task 완료 확인
2. Status를 "Completed"로 변경
3. Archive Info 섹션 작성
4. `archive/YYYY-MM/` 폴더로 이동

## PRD Status

| Status | Description |
|--------|-------------|
| Planning | 계획 수립 중 |
| In Progress | 개발 진행 중 |
| Review | 검토/테스트 중 |
| Completed | 완료 |
| Archived | 아카이브됨 |

## Agent Integration

PRD는 다음 에이전트들과 연동됩니다:

| Agent | Role |
|-------|------|
| `prd-manager` | PRD 생성/관리/아카이빙 |
| `map-feature-specialist` | Map 기능 작업 |
| `db-schema-analyzer` | DB 스키마 작업 |
| `wpf-xaml-specialist` | UI/XAML 작업 |
| `service-architect` | 서비스 설계 작업 |

## Commands

```bash
# 활성 PRD 목록 확인
ls PRDs/active/

# PRD 아카이빙 (2025년 12월)
mkdir -p PRDs/archive/2025-12/
mv PRDs/active/feature-xxx.prd PRDs/archive/2025-12/
```

## Best Practices

1. **작은 단위**: 각 PRD는 1-2주 내 완료 가능한 크기로
2. **명확한 기준**: 완료 기준을 구체적으로 명시
3. **진행 기록**: Progress Log를 꾸준히 업데이트
4. **에이전트 학습**: 작업 결과를 에이전트 파일에 기록
5. **정기 정리**: 완료된 PRD는 월별로 아카이빙
