# Unity Content Authoring Tool

Unity Editor 內容檢視、美術資產瀏覽與 Prefab Authoring 工具。支援從範例資料、ScriptableObject 與 Excel 載入內容，檢查資料關係，並從 Base Template 建立含 Nested Model 與動態 Reference 的 Prefab Variant。

本專案以寵物資料為範例，探索可移植至其他專案的工具架構，適合希望了解 UI Toolkit、資料來源抽象與編輯器工具設計的 Unity 開發者。目前仍屬學習與實驗性質；尚未提供的能力列於 [Planned Features](#planned-features)，開發與資產安全規範見 [AGENTS.md](AGENTS.md)。

## 主要功能

- **內容檢視**：切換資料來源，查看內容欄位與來源資訊。
- **資料檢查**：辨識缺少或重複 ID、進化目標不存在、自指向與循環關係。
- **美術資產瀏覽**：掃描 Prefab、Sprite、Material 與 Animator Controller。
- **搜尋與篩選**：依資產名稱或路徑搜尋，並搭配資產類型篩選。
- **預覽與定位**：顯示資產預覽或縮圖，透過 Ping 與 Select 定位資產。
- **Prefab Reference Authoring**：建立含 Nested Model 的 Variant，編輯動態 Reference 並驗證儲存後的物件身分。操作流程見 [Create Content / Prefab Variant](#create-content--prefab-variant)。

## 環境需求

| 項目 | 說明 |
| --- | --- |
| Unity | `6000.3.9f1` |
| 編輯器介面 | UI Toolkit |
| Excel 讀取 | NPOI，讀取 `.xlsx` 格式 |
| 套件管理 | UPM 依賴見 [manifest.json](Packages/manifest.json)；NPOI 等 .NET 套件位於 [Assets/Packages](Assets/Packages) |

這是 Unity Editor 工具，主要操作不需要進入 Play Mode。

## 快速開始

1. 使用 Unity `6000.3.9f1` 開啟專案，等待套件還原、資產匯入與編譯完成。
2. 從 `Tools > Content Tools > Art Content Tool` 開啟工具。
3. 在預設的 `Content` 模式查看 Fake Source 資料，選取項目查看內容、來源及檢查訊息。
4. 使用 `SO Source`、`Fake Source` 或 `Excel Source` 切換資料來源。
5. 切至 `Art Assets`，點擊 `Rescan Assets` 掃描美術資產。
6. 在 `Search` 輸入資產名稱或路徑片段，使用 `Type` 選擇類型；選取結果查看預覽與詳細資訊。
7. 要建立新 Prefab，點擊上方 **Create Content**，依下方操作流程開啟獨立建立視窗。

Fake Source 刻意包含重複 ID、缺少進化目標、自指向與循環關係等案例，供檢查規則使用。

## 資料來源與 Excel 格式

| 來源 | 資料取得方式 |
| --- | --- |
| Fake Source | 程式內建立範例資料，也是開啟工具時的預設來源 |
| SO Source | 透過 AssetDatabase 搜尋專案中的 `PetData`，轉換成共用內容模型 |
| Excel Source | 讀取固定路徑 `Assets/Game/ContentData/Content.xlsx` 的 `PetSetting` 工作表 |

內容列表顯示名稱與檢查狀態；詳細區域顯示 ID、名稱、Prefab key、Icon key 與來源資訊。

來源解析診斷與內容規則分開處理：Excel 的欄位／型別診斷輸出至 Unity Console；內容規則結果顯示在列表與詳細區域。

### Excel 格式

Excel 的**第二列作為欄名，第三列開始作為資料**；第一列不參與解析。欄名須與下表一致。

| 欄名 | 目前用途 | 型別 |
| --- | --- | --- |
| `Id` | 內容 ID | string |
| `Name` | 顯示名稱 | string |
| `ContentType` | 內容類型 | int |
| `PrefabAddress` | 映射為 `ContentDefinition.PrefabKey` | string |
| `IconKey` | 圖示識別值 | string |
| `EvolutionFromId` | 前一階段內容 ID | string |
| `EvolutionToId` | 下一階段內容 ID，供進化關係檢查使用 | string |

請提供 `Id`、`Name`、`PrefabAddress`、`IconKey` 與 `ContentType` 欄。前四欄會在載入時檢查欄名；`ContentType` 也為逐列解析所需，缺少此欄時資料列不會載入。進化關係欄可依資料需求提供。

`Id`、`Name` 與 `ContentType` 會檢查儲存格型別，不支援公式；其中 `ContentType` 必須可解析為整數。解析失敗的資料列會略過，錯誤資訊輸出至 Console。其餘欄位以儲存格文字表示讀取，建議直接填入文字值。

檔案不存在、標題列缺失與工作表不存在等情境，容錯和診斷仍待完善。

## 美術資產瀏覽

掃描目錄固定為 `Assets/Game/Arts`。點擊 `Rescan Assets` 後，可使用以下操作：

- **Search**：依資產名稱或路徑片段搜尋，不區分大小寫；清空後顯示所有符合類型條件的資產。
- **Type**：選擇 All、Prefab、Sprite、Material 或 AnimatorController，並可同時搭配搜尋。
- **詳細資訊**：選取資產後顯示名稱、類型、路徑與 GUID。優先顯示 Unity 提供的預覽，未取得預覽時改用縮圖。
- **Ping／Select**：分別在 Project 視窗提示資產位置，或將其設為 Unity 目前選取的資產。

變更搜尋或類型條件後，詳細區域會清空，請重新選取資產。Art Assets 列表獨立於內容來源；掃描後，Content 詳細面板的 **Asset Mapping** 會依資產類型與名稱比對 Prefab／Icon key，顯示 Not scanned、Missing、Matched 或 Ambiguous。多個同名資產不會自動選第一個；符合單一 Prefab 後還會顯示 Prefab Contract 與 Runtime Address 檢查。這些是唯讀檢查，不會執行 Addressables 註冊。

## Create Content / Prefab Variant

從 `Tools > Content Tools > Art Content Tool` 開啟主視窗，再點 **Create Content**。此功能建立新 Prefab，不回寫 Excel／PetData，也不把 Draft 自動加入 Content 列表。

### 必要條件

| 欄位 | 要求 |
| --- | --- |
| Content Type (integer) | 32-bit 整數，包含 0；用來決定輸出資料夾，沒有內建角色／寵物類型對照。 |
| Content ID | 去除首尾空白後不可為空；不能包含控制字元、`<>:"/\|?*` 或結尾句點。 |
| Base Template Prefab | Project 視窗中的 Regular／Variant Prefab 根物件，已有 `ContentPrefabReferences`。不接受 Scene instance 或 FBX Model 作為 Base Template。 |
| Source Model Prefab | Project 中的 Regular／Variant／匯入 Model Prefab 根物件；不接受 Scene instance 或 Prefab 的子物件。 |

工具不自動新增 Reference component；自行準備 Template 時，需先配置該公開 component。Reference 面板會列出所有既有 container；若同時要通過 Content Browser 的 Prefab Contract，root 上也需有 component 並滿足其獨立檢查。

可先使用可重用範例：

- Base Template：`Assets/Game/Tests/Fixtures/ReferenceAuthoring/BaseTemplate.prefab`
- Source Model：`Assets/Game/Tests/Fixtures/ReferenceAuthoring/SourceModel.prefab`

這些是多 Renderer／多 container 的測試素材；只選取它們作為來源，不要直接改寫 fixtures。

### 操作步驟

1. 輸入 Content Type 與全新 Content ID，例如 `3`、`example_001`。
2. 指派 Base Template 與 Source Model。選擇 Template 後會讀取既有 Reference；Source Model 變更時會重新列出 SkinnedMeshRenderer（包含 inactive 子物件），不會自動綁定第一個 Renderer。
3. 查看 **Reference Contract / Draft**。每個 container 顯示 Entry、Template 原值與 Inherited／Added／Override 狀態；未修改的有效值會繼承。原先指向 Template 內舊 Model 的值不會因為加入新 Model 而自動替換。
4. 需要使用新模型時，將固定 `animator`／`mainRenderer` 的 **Value Source** 改為 **SourceModel**，再從 **Model Object** 明確選擇對應 Animator／SkinnedMeshRenderer。固定欄位可改值，但不可刪除或改 Key／Type。
5. 依下節新增或調整動態 Entries。完成延遲文字欄位（Entry Key／String Value）後按 Enter 或移開焦點，確認診斷已更新。
6. 查看欄位驗證與 **Output** 路徑。必要資料不完整、Reference 失效、Key 重複或輸出已存在時，**Create Variant** 會停用並顯示原因。
7. 按 **Create Variant**。工具建立 Variant、加入 Nested Model、寫入 References，儲存後重新載入比對；未通過不會顯示成功。
8. 成功後使用 **Ping Created Variant** 在 Project 中定位，或 **Select Created Variant** 選取產物。

Draft 只屬於建立視窗，關閉後不保證保留。改選 Template 或點 **Inspect / Reset References (discards reference edits)** 會重建 Reference Draft；Template 在外部修改後需重新 Inspect。改選 Source Model 後，舊的 SourceModel 選擇會顯示失效，必須手動重選。Reference 編輯支援 Unity Undo／Redo。

### Dynamic Reference Authoring

在所屬 container 點 **Add Entry**，設定 **Entry Key**、**Value Type** 及值；用 **Remove Entry (Draft only)** 移除動態 Entry。移除僅影響 Draft／新 Variant，不會刪除來源資產。

- Key 是字串且區分大小寫；同一 container 內不可空白、不可有首尾空白、不可重複，亦不可占用其固定欄位 key。不同 container 可使用同一個 key。
- Value Type 可選 Object、GameObject、Transform、SkinnedMeshRenderer、Animator、Texture、Animation、AnimationClip 或 String。
- String 使用獨立的 **String Value**；它不是 Unity Object，不需要從物件選擇器指定。

Object 類型的 Value Source：

| 選項 | 操作與結果 |
| --- | --- |
| None | 明確清為 null。 |
| Template | 在 **Template Object** 選原 Template 階層中的物件，新 Variant 會指向自己的對應物件。 |
| SourceModel | 在 **Model Object** 明確選擇來源 Renderer、Transform 等，新 Variant 會指向新 Nested Model 中的對應 instance。 |
| Asset | 以 **Object Value** 指派 Texture、AnimationClip 等外部 Project 資產，保留原資產連結；外部 Prefab 的 GameObject／Component 不能冒充此類資產。 |

例如新增 `display_mesh`（SkinnedMeshRenderer／SourceModel）、`portrait_texture`（Texture／Asset）、`display_note`（String）。同名物件會附階層與 component 序號供辨識；不需為工具重新命名 Renderer，也不能只憑名稱假定綁定正確。

### 輸出與限制

輸出固定為：

```text
Assets/Game/GeneratedContent/Type_<ContentType>/Content_<Content ID>.prefab
```

例如 `3`／`example_001` 會產生 `Assets/Game/GeneratedContent/Type_3/Content_example_001.prefab`。路徑被 Prefab、其他檔案、目錄或孤立 `.meta` 占用時均拒絕建立，不覆寫或自動換名。

- 保留 Variant 的 Template Base Link 與 Nested Model Source Link；原始 Template／Model 不被修改。
- 儲存後檢查 Reference Key／Type／Value 與物件身分；錯誤會說明 Key、來源及原因，不能把「檔案存在」當作驗證成功。
- None 可以代表空值，但不代表能通過 Content Browser 對 Animator Controller、Renderer bones 等獨立的必填規則。
- GeneratedContent 不在 Art Assets 的固定掃描目錄 `Assets/Game/Arts` 內；請以 Ping／Select 或 Project 視窗找產物。
- 只支援既有公開 Generic Reference Contract，不編輯任意第三方 Collector，不重建 FBX 骨架／bones，也不自動配置動畫控制器。
- 不做 Addressables Registration、Excel Export、Delete／Quarantine 或內容來源回寫。Runtime Address 面板只檢查現有註冊狀態。
- 儲存後驗證失敗時可能已留下新 Prefab，工具不會自動刪除；先檢查結果及 Console，再決定下一步。相同 ID 不可直接覆寫重試。
- 尚無跨程序寫入鎖，請勿同時讓其他工具寫入相同輸出路徑。

開發者回歸入口為 `Tools > Art Content Tool` 下的 M2.1 Safe Checks、M2.2 Reference Checks、Apply Preview Checks、Save Reload Checks；驗證要求與 fixture／輸出清理規範見 [AGENTS.md](AGENTS.md)。

## 架構與擴充

資料來源透過 `IContentSource.Load()` 回傳 `ContentLoadResult`，其中包含內容記錄與來源解析診斷。

- `IContentSource` 封裝不同來源的載入方式，回傳資料與來源診斷。
- `ContentRecord` 組合共用內容定義與 `ContentSourceHandle`，保留來源追溯資訊。
- `ContentDefinition` 描述內容本身；EditorWindow 負責呈現、切換來源與執行內容規則。
- `AssetRecord` 描述原始美術資產，與內容資料分開管理。

| 目錄 | 責任 |
| --- | --- |
| [Assets/Game/Editor/ArtContentTool](Assets/Game/Editor/ArtContentTool) | 工具介面、來源轉接、來源模型、內容檢查與資產瀏覽 |
| [Assets/Game/Runtime/Content](Assets/Game/Runtime/Content) | 共用內容定義與 `PetData` |

新增資料來源時，可實作 `IContentSource`，將來源資料轉為 `ContentRecord`，再加入視窗的來源切換流程。移植至其他專案時，也需要調整 `PetData` 轉換、Excel 路徑與工作表、美術掃描目錄，以及資產識別方式。

## 已知限制

- 目前內容介面以讀取與檢視為主，尚無跨來源的編輯回寫或匯出流程。
- SO 來源使用 Prefab／Icon 的物件名稱作為 key，名稱不保證唯一，也不是可直接載入資產的位址。
- Excel 路徑、工作表名稱與美術掃描目錄目前固定於程式中。
- 來源診斷與內容規則已有區分，但規則與部分 UI／資料處理仍集中在視窗類別。
- Excel 容錯、診斷列號與必要欄位定義仍待統一；不建議直接套用未符合上述格式的工作表。


## Planned Features

以下尚未實作或仍待完善，沒有承諾發布時程：

- 完善 Excel 檔案／工作表／標題列容錯，統一必要欄位、診斷列號與儲存格解析策略。
- 將來源路徑、工作表名稱與美術掃描目錄改為可設定。
- 將仍集中於主視窗的內容規則與資產處理拆分為可測試邏輯。
- 在既有名稱比對基礎上完善內容 key／資產識別，再評估來源專屬編輯、回寫與匯出。

## 第三方資源

此專案有使用第三方資源進行 Unity Editor Tooling 開發、測試、展示。

### Quaternius — Ultimate Animated Character Pack - Nov 2019

- Creator: Quaternius
- Source: https://quaternius.com/packs/ultimatedanimatedcharacter.html
- License: Creative Commons Zero (CC0 1.0)
- Usage: Character model, skeleton, renderer discovery, and prefab authoring tests.
- License record: ThirdParty/Quaternius/License - Ultimate Animated Character Pack.txt
- Acquired on: 2026-10-28

### Quaternius — Pirate Kit - Nov 2023

- Creator: Quaternius
- Source: https://quaternius.com/packs/piratekit.html
- License: Creative Commons Zero (CC0 1.0)
- Usage: Character model, skeleton, renderer discovery, and prefab authoring tests.
- Acquired on: 2026-10-28