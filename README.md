### ⚠️ IMPORTANT NOTICE / DISCLAIMER

**Original Author:** AshleySchaefferBMW
**Original Repository:** AshleySchaeffer-Additional-Gear-Clothing-4.0.13
**Original Link:** https://github.com/AshleySchaefferBMW/AshleySchaeffer-Additional-Gear-Clothing-4.0.13
**License:** The Unlicense (public domain)
**This Port By:** R_F (danyhappy564-cmyk) — unofficial, AI-assisted port. Not affiliated with or endorsed by the original author.

1. **Reflection & Take-Downs:** I deeply reflect on the ECOT incident. As an AI-assisted "vibe coder," I will immediately delete files if the original authors ask.
2. **No Re-Distribution:** These ported builds are unverified, temporary fixes. Please do NOT re-upload or share them anywhere else.
3. **Do Not Pester Original Authors:** Never report bugs or pester original modders regarding issues from my unofficial ports.
4. **Full Credit & Respect:** I will always credit original creators on GitHub and prioritize their decisions above all else.
5. **Support Original Creators:** Instead of using my ports, please visit the original authors' Forge pages to leave kind words or tips.

---

# Ashley Schaeffer — Additional Gear and Clothing (SPT 4.1 포팅)

BEAR/USEC용 **장비 27종 + 상의 29벌 + 하의 23벌**을 추가하고, 그걸 파는 상인
**Ashley Schaeffer**를 넣는 서버 모드입니다. 상인을 끄면 옷은 라그만이 대신 팝니다.

> **원작자 · 라이선스**
> **AshleySchaefferBMW** — **Unlicense (퍼블릭 도메인)**
>
> 이 저장소는 위 원작의 **포크**입니다. `LICENSE` 파일은 손대지 않았습니다.

---

## ⚠️ 이 포팅은 디컴파일로 만들었습니다

원본 저장소에는 **소스 코드가 없었습니다.** 컴파일된 `AshleySchaeffer.dll`(net9.0,
`SPTarkov.Server.Core 4.0.0.0`) 하나랑 JSON 데이터만 있었습니다. 그래서 **DLL을
디컴파일해서 소스를 복원한 다음 4.1로 포팅**했습니다.

원작 라이선스가 **Unlicense** 라서 이게 명시적으로 허용됩니다:

> Anyone is free to copy, modify, publish, use, **compile**, sell, or distribute this software,
> either in **source code form** or as a compiled binary, for any purpose...

원본 DLL은 대조용으로 `vendor/AshleySchaeffer.dll` 에 그대로 남겨뒀습니다.

### 복원이 제대로 됐는지 어떻게 확인했나

**1. 상수 전량 대조.** 디컴파일 → 재작성 과정에서 제일 위험한 건 MongoId 한 글자나
숫자 하나가 틀리는 겁니다. 그래서 원본 DLL과 새로 빌드한 DLL에서 **IL 레벨 리터럴을
전부 뽑아 비교**했습니다:

```
strings: 4.0 = 59, 4.1 = 56
numbers: 4.0 =  9, 4.1 =  8
```

빠진 것 전부 설명됩니다:

| 빠진 리터럴 | 이유 |
|---|---|
| `"4.0.0"`, `"~4.0.3"` | 버전 표기 → `4.1.0` / `~4.1.0` |
| `"ModMetadata"`, `" { "`, `-1521134295` | 4.0의 `AbstractModMetadata`가 `record`였는데 4.1은 `IModMetadata` 인터페이스라 `record` 자동생성 코드(`ToString`/`GetHashCode`/`PrintMembers`)가 통째로 사라짐 |
| `"tid"` | **의도적 버그 수정** — 아래 참고 |

즉 MongoId 20개, 번들 경로, 로케일 문자열, 로그 문구, 숫자 전부 **한 글자도 안 틀렸습니다.**

**2. 실제로 돌려봤습니다.** `tests/` 에 통합 테스트 **20개**가 있습니다. 가짜 데이터가
아니라 **이 저장소에 실제로 들어있는 `mod/` JSON**을 그대로 먹이고, 진짜
`AshleySchaefferLoader.OnLoadAsync()` 를 실행한 뒤 DB에 뭐가 들어갔는지 검사합니다.

```
dotnet test AshleySchaeffer.slnx -c Release
→ Passed! - Failed: 0, Passed: 20
```

검사 항목: 장비 27종 복제·핸드북 등록·로케일 3종, 옷 52벌의 메쉬+수트 쌍,
수트 가격/충성도/레벨/평판, 상인 등록·갱신 주기(1~2시간)·플리 등록,
봇 장비 확률 상속, 봇 옷장, 플리 프리셋 15개, 진영 해금 on/off, 각 config 스위치 on/off.

---

## 4.1 포팅에서 바뀐 것

### API — `DatabaseTables` / `ConfigServer` 가 통째로 없어짐

4.1은 DB 전체를 `DatabaseService.GetTables()` 로 받아오는 방식을 없애고 **필요한 테이블만
직접 주입**받게 바뀌었습니다. 설정도 마찬가지로 `ConfigServer.GetConfig<T>()` 대신 직접 주입입니다.

| 4.0 | 4.1 |
|---|---|
| `DatabaseService.GetTables().Templates` | `TemplateTable` 주입 |
| `...Locales` | `LocaleTable` 주입 |
| `...Bots` | `BotTable` 주입 |
| `...Traders` | `TradersTable` 주입 |
| `...Globals` | `GlobalTable` 주입 |
| `configServer.GetConfig<TraderConfig>()` | `TraderConfig` 주입 |
| `SPTarkov.Server.Core.Helpers.ModHelper` | `...Helpers.**Server**.ModHelper` |
| `Task OnLoad()` | `Task OnLoadAsync(CancellationToken)` |
| `AbstractModMetadata` (record) | `IModMetadata` (인터페이스) |

`IModMetadata`는 멤버도 바뀌었습니다 — `IsBundleMod` 는 **없어졌고**(4.1은 `bundles.json`
존재 여부로 판단합니다) `HasPrepatcher` 가 생겼습니다.

`BotTable.Types` 도 `Dictionary<string, BotType>` → `Dictionary<string, BotType?>` 로 바뀌어서
null 체크가 들어갔습니다.

### 로드 순서 — 이게 제일 중요합니다

원작은 `OnLoadOrder.PostDBModLoader + 1` (= 400001) 에서 돌았습니다. 4.1은 그 단계 이름
자체가 없어졌고, 더 중요하게 **하드 컷오프가 생겼습니다.** `DatabaseIntegrityService` 가
프로필 로드 시점에 `Templates.Items` 를 스냅샷 떠놓고, 그 뒤에 추가된 아이템이 있으면
서버를 죽입니다:

> `DatabaseModifiedAfterCutoffException`: N item(s) were added to the database after profiles
> loaded... Add items during **OnLoadOrder.Preload**, the database is fully loaded by then

이 모드는 장비 27종을 `Templates.Items` 에 복제해 넣기 때문에 **반드시 `Preload`** 여야
합니다. 그래서 `OnLoadOrder.Preload + 1` 로 옮겼습니다 (뒤의 `+ 1` 은 원작 그대로).

Preload 시점에 이미 **DB 전체가(상인 포함) 로드돼 있다는 건 SPT 코드가 직접 보증**합니다
— 위 에러 메시지의 "the database is fully loaded by then" 이 그겁니다. 그래서 이 단계에서
라그만의 수트 목록을 읽어오는 것도 안전합니다.

### `InjectionType` 열거형 번호가 밀렸습니다

4.0과 4.1의 기본값이 **둘 다 2인데, 2가 가리키는 게 다릅니다**:

| 값 | 4.0 | 4.1 |
|---|---|---|
| 0 | Singleton | **HostedService** |
| 1 | Transient | **Singleton** |
| 2 | **Scoped** | **Transient** |
| 3 | — | Scoped |

원작은 `[Injectable(TypePriority = ...)]` 로 **기본값을 그대로 썼고**, SPT 본체의
`GameCallbacks` / `SaveCallbacks` / `TraderCallbacks` 같은 것들도 전부 같은 기본값을
씁니다. 그래서 여기서도 명시하지 않고 기본값을 그대로 유지했습니다.

---

## 의도적으로 고친 버그 하나 — 상의의 `tid`

**원작 코드가 상의와 하의를 다르게 처리하고 있었습니다.**

```csharp
// AddBottomItem — 맞음
suit.Tid = sellerId;

// AddTopItem — 틀림
suit.ExtensionData["tid"] = sellerId;
```

`Suit.Tid` 에는 이미 `[JsonPropertyName("tid")]` 가 붙어 있습니다. 그래서 위 코드는

1. `"tid"` 키를 **두 번** 쓰고,
2. 정작 `Tid` 자체는 **복제해온 값 = 라그만 ID** 그대로 남깁니다.

상의는 라그만의 수트를 `cloner.Clone()` 해서 만들기 때문입니다. 결과적으로 상인을 켜면
**상의 29벌이 "나는 라그만 물건이다"라고 주장하면서 요구조건만 Ashley를 가리키는** 상태가
됩니다.

포팅하면서 하의와 똑같이 `suit.Tid = sellerId` 로 맞췄습니다. 위 IL 상수 비교에서 `"tid"`
문자열이 사라진 게 이것 때문이고, `Every_suit_belongs_to_the_trader_that_sells_it_tops_included`
테스트가 이걸 고정하고 있습니다.

**이것 말고는 동작이 원작과 100% 같습니다.**

---

## 상인이 안 보이던 문제 (4.1.1에서 수정)

**증상:** 모드는 정상 로드되고 서버 로그에 에러도 없는데, 게임 안에서 상인 목록에
Ashley Schaeffer가 아예 안 나옵니다.

**원인:** `db/base.json` 에 **`isAvailableInPVE` 키가 없었습니다.**

SPT는 세션을 무조건 PVE로 고정합니다 (`GameController.GetGameMode()` 가 `"pve"` 를
하드코딩). 그리고 `TraderBase.IsAvailableInPVE` 는 **nullable이 아닌 `bool`** 이라,
JSON에 키가 없으면 `false` 가 되고 그대로 클라이언트에 내려갑니다. 클라이언트는
PVE 모드에서 이 플래그가 `false` 인 상인을 목록에서 제외합니다.

서버는 이 필드를 **한 번도 읽지 않기 때문에** 경고도 에러도 안 납니다. 상인은
`TradersTable` 에 정상 등록되고, 어사트도 로케일도 다 들어가 있는데 화면에만 안
나오는 상태가 됩니다.

SPT 4.1.5 기본 데이터베이스의 상인 12명(프라포르·테라피스트·펜스·스키어·피스키퍼·
메카닉·라그만·예거·caretaker·БТР·Arena·Storyteller)을 전부 대조한 결과 **예외 없이
`isAvailableInPVE: true`** 였습니다.

**수정:** 기본 상인들이 갖고 있는데 `base.json` 에 빠져 있던 필드를 전부 채웠습니다.

| 필드 | 값 | 비고 |
|---|---|---|
| `isAvailableInPVE` | `true` | **이게 상인을 숨기고 있던 범인입니다** |
| `isCanTransferItems` | `false` | PVE→PVP 이관용. SPT에선 의미 없음 |
| `isCanTransferItemsFromPve` | `false` | 위와 동일 |
| `transferableItems` | `{category:[], id_list:[]}` | 기본 상인과 동일한 빈 형태 |
| `prohibitedTransferableItems` | `{category:[], id_list:[]}` | 위와 동일 |
| `sell_modifier_for_prohibited_items` | `0` | 기본 상인 값 |
| `medic` | `false` | 기본 상인 값 |
| `mainDialogue` | `null` | 기본 상인 값 |

> **4.0 포팅 회귀는 아닙니다.** `isAvailableInPVE` 는 SPT 4.0.0 시점의
> `TraderBase` 에도 이미 **nullable이 아닌 `bool`** 로 있었고, 4.0도 세션을 `"pve"`
> 로 고정합니다. 즉 원작 4.0 배포본도 같은 상태였을 가능성이 높습니다. 제가 포팅하면서
> 깨뜨린 게 아니라, 원작 `base.json` 이 원래 이 키를 안 갖고 있었던 겁니다.

**초상화 처리도 같이 바꿨습니다.** 예전에는 `res/AshleySchaeffer.jpg` 가 없어도
그 경로로 이미지 라우트를 무조건 등록해서, 클라이언트가 없는 파일을 요청하게
됐습니다. 이제는 파일이 있을 때만 등록하고, 없으면 경고를 찍은 뒤 바닐라 기본
초상화(`/files/trader/avatar/unknown.png`)로 대체합니다.

---

## 모드 전체가 비활성화되던 문제 (4.1.2에서 수정)

**증상:** 서버 시작 시 아래가 뜨고 **설치된 모드 전부가 꺼집니다.**

```
Mod: AshleySchaefferBMW-Ashley Schaeffer Additional Gear and Clothing has an invalid mod guid:
com.AshleySchaefferBMW.Ashley Schaeffer Additional Gear and Clothing
모드를 불러오는 중에 오류가 발생하였습니다, 모든 모드가 비활성화되었습니다
```

**원인:** `ModGuid` 에 **공백**이 들어 있었습니다. 4.0 값을 그대로 승계한 게 문제였습니다.

SPT 4.1의 `ModValidator` 는 모든 모드 GUID를 다음 정규식으로 검사합니다:

```csharp
// SPTushonka.Server/Modding/ModValidator.cs
[GeneratedRegex("^[a-zA-Z0-9-]+(\.[a-zA-Z0-9-]+)*$")]
private static partial Regex ModGuidRegex();
```

영문·숫자·하이픈만 허용하고 점으로 구분합니다. **공백과 언더스코어는 불가**입니다.

그리고 이건 해당 모드만 죽는 문제가 아닙니다. 검증에 실패하면 `errorsFound = true` 가
되고, 그 뒤 **빈 목록을 반환**합니다:

```csharp
if (errorsFound)
{
    logger.Error(localisationService.GetText("modloader-no_mods_loaded"));
    return [];   // ← 설치된 모드 전부가 로드되지 않음
}
```

**수정:** `com.AshleySchaefferBMW.AshleySchaefferAdditionalGearAndClothing`

테스트로 고정했습니다 — 서버와 동일한 정규식을 테스트에 복사해 GUID를 검사하고,
그 정규식 사본이 서버 것과 어긋나지 않았는지도 별도 케이스로 확인합니다.

---

## 같은 이름의 DLL이 두 번 로드될 때

**증상:**

```
Exception occured while loading a mod at path: ./user/mods/AshleySchaeffer
Could not load file or assembly 'AshleySchaeffer, Version=4.1.0.0, ...'.
Assembly with same name is already loaded
```

**원인:** `ModLoader.LoadMod()` 는 각 모드 폴더 **최상위의 모든 `.dll`** 을 **공유 로드
컨텍스트**에 올립니다. 따라서 같은 이름의 DLL이 **서로 다른 모드 폴더 두 곳**에 있으면
두 번째가 실패합니다.

이건 코드 문제가 아니라 **설치 상태 문제**입니다. `user/mods/` 아래에서
`AshleySchaeffer.dll` 을 검색해 **폴더가 하나만 남도록** 정리하면 됩니다.

> 참고: 이 저장소의 빌드 배포 타겟은 서버가 켜져 있으면 DLL을 덮어쓰지 못하고
> **경고만 내고 지나갑니다**(`ContinueOnError="WarnAndContinue"`). 그래서 빌드는
> 성공했는데 배포 폴더에는 옛 버전이 남아 있을 수 있습니다.
> **서버를 끈 상태에서 빌드**하시거나, `-p:SkipDeploy=true` 로 배포를 건너뛰고
> 직접 복사하십시오.

---

## 남아있는 원작 버그 (안 고쳤습니다)

고치면 게임이 눈에 띄게 달라지는 것들이라 **원작 그대로 뒀습니다.** 원하시면 말씀하세요, 고쳐드립니다.

### 1. `pmcsUseFactionedGearOnly` 를 켜면 PMC가 신규 장비를 하나도 안 입습니다

```csharp
if (isPmc && config.pmcsUseFactionedGearOnly && (gearItem.side is null || !botName.Equals(gearItem.side)))
    continue;
```

`botName` 은 봇 테이블 키라서 **소문자** (`"bear"`, `"usec"`) 인데 `itemData.json` 의
`side` 는 **`"Bear"`** 입니다. `string.Equals` 는 대소문자를 가리므로 **영원히 안 맞습니다.**
→ BEAR도 USEC도 전부 `continue` 로 건너뜁니다.

그리고 **27개 장비 전부가 side 를 달고 있습니다** (26개가 `"Bear"`, 1개가 `"Bear, usec"` —
이건 봇 이름이 아예 아니라서 어떤 비교로도 안 맞습니다). `pmcsUseFactionedGearOnly` 는
**config.json 기본값이 `true`** 입니다.

즉 **기본 설정에서 PMC는 신규 장비를 하나도 안 입습니다.** 스캐브·보스·레이더는 정상적으로
입습니다 (이 필터는 PMC에만 걸리니까).

당장 쓰시려면 `config.json` 에서 `"pmcsUseFactionedGearOnly": false` 로 두면 PMC도 전부
입습니다. 코드로 고치려면 `botName.Equals(gearItem.side, StringComparison.OrdinalIgnoreCase)`
한 줄입니다.

테스트 `Faction_locking_keeps_tagged_gear_off_BOTH_PMC_factions_a_known_bug` 가
이 동작을 고정해두고 있어서, 나중에 누가 모르고 바꾸면 테스트가 깨집니다.

### 2. config.json 에 코드가 안 읽는 키가 2개 있습니다

| 키 | 상태 |
|---|---|
| `pmcsGearWeightMultiplier` | 읽는 코드 없음 |
| `sellClothingOnRagman` | 읽는 코드 없음 |

`itemData.json` 의 장비마다 붙어있는 `"weight"` 필드도 마찬가지로 읽는 코드가 없습니다.
배포된 4.0 DLL의 `ModConfig` 클래스에는 스위치가 **7개뿐**이고, 이 3개는 그 안에 없습니다.
JSON이 DLL보다 최신이거나, 만들다 만 기능으로 보입니다. **없는 기능을 제가 지어내지는
않았습니다** — 원작과 똑같이 무시합니다.

---

## 설치 — 직접 빌드하셔야 합니다

```
git pull
dotnet build AshleySchaeffer.slnx -c Release
```

빌드하면 `src/AshleySchaeffer/bin/Release/` 에 아래 구조가 그대로 나옵니다:

```
AshleySchaeffer.dll
config.json
bundles.json
db/base.json
db/assort.json
db/items/{itemData,fleaPresets,prices}.json
db/locales/en.json
```

이걸 통째로 `SPT_Runtime\user\mods\AshleySchaeffer\` 에 넣으면 됩니다.
Windows에서 빌드하면 **자동으로 복사**됩니다 (`SptRoot` 기본값 `E:\SPT 4.1`).
경로가 다르면:

```
dotnet build AshleySchaeffer.slnx -c Release -p:SptRoot="D:\내SPT경로"
```

기존 `config.json` 은 덮어쓰지 않습니다.

### ⚠️ 이 저장소에 없는 파일 2가지

이건 제가 만들 수 없습니다. **원작 배포본에서 가져오셔야 합니다.**

1. **번들 파일 110개** — `bundles.json` 에 목록은 다 있는데 실제 `.bundle` 파일이
   저장소에 없습니다 (용량 때문에 GitHub에 안 올린 것으로 보입니다).
   없으면 아이템은 등록되지만 **모델이 안 보입니다.**

2. **`res/AshleySchaeffer.jpg`** — 상인 초상화. `base.json` 이
   `/files/trader/avatar/avatar.jpg` 를 가리키고 코드가 `res/AshleySchaeffer.jpg` 를
   등록합니다. **없어도 상인은 정상적으로 나옵니다** — 4.1.1부터는 파일이 없으면
   바닐라 기본 초상화로 대체하고 서버 로그에 경고를 남깁니다. 원본 초상화를 쓰고
   싶으면 이 파일만 넣으면 됩니다.

둘 다 원작 배포본의 같은 경로에 넣으면 됩니다 (`mod/res/AshleySchaeffer.jpg`,
`mod/bundles/...`). `mod/` 안에 넣으면 빌드가 알아서 같이 복사합니다.

---

## 설정 (`config.json`)

| 키 | 기본값 | 설명 |
|---|---|---|
| `traderEnabled` | `true` | Ashley Schaeffer 상인 추가. `false` 면 옷을 라그만이 팝니다 |
| `sellGearOnFlea` | `true` | 신규 장비를 플리에서 사고팔 수 있게 + `prices.json` 가격 적용 |
| `allGearAvailableOnFlea` | `true` | 위와 같이 켜면 플리 판매·요청 둘 다 허용 |
| `botsUseGear` | `true` | 봇이 신규 장비를 착용 (원본 아이템의 스폰 확률을 그대로 복사) |
| `pmcsUseFactionedGearOnly` | `true` | **위 "남아있는 버그 1" 참고.** 켜면 PMC가 신규 장비를 안 입습니다 |
| `botsUseClothing` | `true` | PMC 봇이 신규 옷을 착용 (각 부위별로 절반만 무작위 추가) |
| `unlockOthersFactionsClothing` | `true` | 바닐라 진영 전용 옷을 BEAR/USEC 양쪽에서 입을 수 있게 |
| `pmcsGearWeightMultiplier` | `1.337` | **읽는 코드 없음** |
| `sellClothingOnRagman` | `false` | **읽는 코드 없음** |

---

## 저장소 구조

```
src/AshleySchaeffer/     복원 + 포팅한 소스
tests/AshleySchaeffer.Tests/   통합 테스트 20개
mod/                     서버가 읽는 JSON (dll 옆으로 복사됨)
vendor/AshleySchaeffer.dll     원본 4.0 DLL (대조용)
LICENSE                  원작 그대로 (Unlicense)
```

## 라이선스

원작 **AshleySchaefferBMW** / **Unlicense (퍼블릭 도메인)**. `LICENSE` 참고.
