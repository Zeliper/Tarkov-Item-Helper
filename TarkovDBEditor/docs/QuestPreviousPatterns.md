# Quest Previous 필드 패턴 정리

Quest Cache 파일의 `|previous` 필드에서 발견된 모든 패턴을 정리한 문서입니다.

## 패턴 분류

### 1. 단일 퀘스트 (Simple Link)
가장 기본적인 패턴으로, 하나의 퀘스트만 선행 조건으로 지정됩니다.

```
|previous     =[[Quest Name]]
```

**예시:**
- `|previous     =[[Pharmacist]]`
- `|previous     =[[The Punisher - Part 6]]`
- `|previous     =[[Debut]]`

---

### 2. 빈 값 (Empty)
선행 퀘스트가 없는 경우입니다.

```
|previous     =
```

---

### 3. 복수 퀘스트 - AND 조건 (`<br/>` 또는 `<br>`)
여러 퀘스트를 **모두** 완료해야 하는 경우입니다. `<br/>`, `<br>`, `</br>` 태그로 구분됩니다.

```
|previous     =[[Quest A]]<br/>[[Quest B]]
|previous     =[[Quest A]]<br>[[Quest B]]
|previous     =[[Quest A]]</br>[[Quest B]]
```

**예시:**
- `|previous     =[[Anesthesia]]<br/>[[Fishing Gear]]`
- `|previous     =[[Make ULTRA Great Again]]<br/>[[Big Sale]]`
- `|previous     =[[Broadcast - Part 2]]<br>[[Corporate Secrets]]`
- `|previous     =[[Colleagues - Part 2]]<br />[[Rigged Game]]<br />[[Chemistry Closet]]`
- `|previous     =[[Sales Night]]</br>[[The Huntsman Path - Forest Cleaning]]`
- `|previous     =[[Missing Cargo]]</br>[[Getting Acquainted]]`

---

### 4. OR 조건 (`or` 키워드)
여러 퀘스트 중 **하나만** 완료하면 되는 경우입니다.

```
|previous     =[[Quest A]]<br/>or<br/>[[Quest B]]
```

**예시:**
- `|previous     =[[Swift Retribution]]<br/>or<br/>[[Inevitable Response]]`
- `|previous     =[[Chemical - Part 4]]<br/>or<br/>[[Big Customer]]`
- `|previous     =[[Chemical - Part 4]]<br/>or<br/>[[Out of Curiosity]]`
- `|previous     =[[Kind of Sabotage]]<br/>or<br/>[[Supply Plans]]`
- `|previous     =[[Make Amends - Equipment]]<br/>or<br/>[[Make Amends - Sweep Up]]<br/>or<br/>[[Make Amends - Quarantine]]`
- `|previous     =[[Stick in the Wheel]]<br/>or<br/>[[Stabilize Business]]`
- `|previous     =[[One Less Loose End]]<br/>or<br/>[[A Healthy Alternative]]`

---

### 5. 혼합 조건 (AND + OR)
AND와 OR 조건이 함께 사용되는 복잡한 경우입니다.

```
|previous     =[[Quest A]]<br/>[[Quest B]]<br/>or<br/>[[Quest C]]
```

**예시:**
- `|previous     =[[Chemical - Part 4]]<br/>or<br/>[[Out of Curiosity]]<br/>or<br/>[[Big Customer]]`
- `|previous     =[[Samples]]<br/>[[The Huntsman Path - Sadist]]<br/>or<br/>[[Colleagues - Part 3]]`
- `|previous     =[[The Huntsman Path - Sadist]]<br/>or<br/>[[Colleagues - Part 3]]`
- `|previous     =[[The Huntsman Path - Secured Perimeter]]<br/>[[Supply Plans]]<br/>or<br/>[[Kind of Sabotage]]`
- `|previous     =[[The Huntsman Path - Trophy]]<br/>[[The Huntsman Path - Woods Keeper]]<br/>[[The Huntsman Path - Eraser - Part 1]]<br/>[[The Huntsman Path - Sellout]]<br/>[[The Huntsman Path - Factory Chief]]<br/>[[The Huntsman Path - Sadist]]<br/>or<br/>[[Colleagues - Part 3]]`

---

### 6. Accept 조건
특정 퀘스트를 **수락**만 하면 되는 경우입니다 (완료 필요 없음).

```
|previous     =Accept [[Quest Name]]
```

**예시:**
- `|previous     =Accept [[Revision - Reserve]]`
- `|previous     =Accept [[A Helping Hand]]`
- `|previous     =Accept [[Wet Job - Part 5]]`
- `|previous     =Accept [[The Tarkov Shooter - Part 1]]`
- `|previous     =Accept [[The Punisher - Part 5]]`
- `|previous     =Accept [[The Huntsman Path - Woods Keeper]]`
- `|previous     =Accept [[The Good Times - Part 1]]`
- `|previous     =Accept [[Swift Retribution]]`
- `|previous     =Accept [[Sensory Analysis - Part 1]]`
- `|previous     =Accept [[Saving the Mole]]`
- `|previous     =Accept [[Minibus]]`
- `|previous     =Accept [[Inevitable Response]]`
- `|previous     =Accept [[Hobby Club]]`
- `|previous     =Accept [[Health Care Privacy - Part 4]]`
- `|previous     =Accept [[Gunsmith - Part 1]]`
- `|previous     =Accept [[Farming - Part 4]]`
- `|previous     =Accept [[Easy Money - Part 2]]`
- `|previous     =Accept [[Disease History]]`
- `|previous     =Accept [[Dandies]]`
- `|previous     =Accept [[Chemical - Part 1]]`
- `|previous     =Accept [[Burning Rubber]]`

---

### 7. Fail 조건
특정 퀘스트를 **실패**해야 하는 경우입니다.

```
|previous     =Fail [[Quest Name]]
```

**예시:**
- `|previous     =Fail [[Hot Wheels]]`
- `|previous     =Fail [[Hot Wheels]]<br/>[[Natural Exchange]]` (Fail + AND 조합)

---

### 8. 시간 지연 조건 (`(+시간)`)
선행 퀘스트 완료 후 일정 시간이 지나야 하는 경우입니다.

```
|previous     =[[Quest Name]] (+시간)
```

**시간 형식 예시:**
- `(+6hr)` - 6시간
- `(+10hr)` - 10시간
- `(+12hr)` - 12시간
- `(+18-24hr)` - 18~24시간 범위
- `(+21hr)` - 21시간
- `(+21-23hr)` - 21~23시간 범위
- `(+24hr)` - 24시간
- `(+30min)` - 30분
- `(+1-9hr)` - 1~9시간 범위
- `(+2hr)` - 2시간

**예시:**
- `|previous     =[[Assessment - Part 3]] (+24hr)`
- `|previous     =[[Following the Bread Crumbs]] (+10hr)`
- `|previous     =[[Gunsmith - Part 13]] (+21hr)`
- `|previous     =[[Gunsmith - Part 22]] (+21-23hr)`
- `|previous     =[[Hidden Layer]] (+30min)`
- `|previous     =[[Hindsight 20/20]] (+6hr)`
- `|previous     =[[House Arrest - Part 1]] (+21hr)`
- `|previous     =[[Information Source]] (+10hr)`
- `|previous     =[[Key Partner]] (+6hr)`
- `|previous     =[[Make an Impression]] (+10hr)`
- `|previous     =[[Missing Informant]] (+10hr)`
- `|previous     =[[Network Provider - Part 1]] (+1-9hr)`
- `|previous     =[[Payback]] (+10hr)`
- `|previous     =[[Provocation]] (+10hr)`
- `|previous     =[[Return the Favor]] (+10hr)`
- `|previous     =[[Route Deviation]] (+6hr)`
- `|previous     =[[Signal - Part 3]] (+2hr)`
- `|previous     =[[Silent Caliber]] (+18-24hr)<br/>[[Hunting Trip]] (+18-24hr)`
- `|previous     =[[Snatch]] (+10hr)`
- `|previous     =[[Spotter]] (+10hr)`
- `|previous     =[[Test Drive - Part 1]] (+12hr)`
- `|previous     =[[The Higher They Fly]] (+6hr)`
- `|previous     =[[Thirsty - Echo]] (+12-13hr)`

---

### 9. 앵커 링크 (Section Reference)
퀘스트 페이지의 특정 섹션을 참조하는 경우입니다.

```
|previous     =[[Quest Name#Section]]
```

**예시:**
- `|previous     =[[Collector#Requirements`
- `|previous     =[[Immunity (quest)`
- `|previous     =[[Keeper's Word#Requirements`
- `|previous     =[[Network Provider - Part 1#Requirements`
- `|previous     =[[Reserve (quest)`

---

## 패턴 요약 테이블

| 패턴 유형 | 형식 | 의미 |
|----------|------|------|
| 단일 퀘스트 | `[[Quest]]` | Quest 완료 필요 |
| 빈 값 | (empty) | 선행 조건 없음 |
| AND 조건 | `[[A]]<br/>[[B]]` | A와 B 모두 완료 필요 |
| OR 조건 | `[[A]]<br/>or<br/>[[B]]` | A 또는 B 중 하나 완료 |
| Accept | `Accept [[Quest]]` | Quest 수락만 필요 |
| Fail | `Fail [[Quest]]` | Quest 실패 필요 |
| 시간 지연 | `[[Quest]] (+시간)` | Quest 완료 후 시간 경과 필요 |
| 섹션 참조 | `[[Quest#Section]]` | 특정 요구사항 섹션 참조 |

---

## 파싱 시 주의사항

1. **HTML 엔티티 변환**: JSON에서 `<br/>`는 `\u003Cbr/\u003E`로 인코딩됨
2. **다양한 BR 태그 형식**: `<br/>`, `<br>`, `<br />`, `</br>` 모두 처리 필요
3. **공백 처리**: 일부 항목에 후행 공백 존재 (예: `[[Another Shipping Delay]] `)
4. **불완전한 링크**: 닫는 `]]`가 누락된 경우 있음 (예: `[[Collector#Requirements`)
5. **복합 조건**: AND와 OR가 혼합된 경우 우선순위 고려 필요
6. **시간 형식**: `(+Xhr)`, `(+X-Yhr)`, `(+Xmin)` 등 다양한 형식 존재
