# Unity Content Authoring Tool

Unity Editor 內容檢視與美術資產瀏覽工具，支援從範例資料、ScriptableObject 與 Excel 載入內容，檢查資料關係，並搜尋、篩選及預覽專案中的美術資產。

本專案以寵物資料為範例，探索可移植至其他專案的工具架構，適合希望了解 UI Toolkit、資料來源抽象與編輯器工具設計的 Unity 開發者。目前仍屬學習與實驗性質，功能演進與設計取捨見 [開發路線與技術演進](ROADMAP.md)。

## 主要功能

- **內容檢視**：切換資料來源，查看內容欄位與來源資訊。
- **資料檢查**：辨識缺少或重複 ID、進化目標不存在、自指向與循環關係。
- **美術資產瀏覽**：掃描 Prefab、Sprite、Material 與 Animator Controller。
- **搜尋與篩選**：依資產名稱或路徑搜尋，並搭配資產類型篩選。
- **預覽與定位**：顯示資產預覽或縮圖，透過 Ping 與 Select 定位資產。

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

變更搜尋或類型條件後，詳細區域會清空，請重新選取資產。此瀏覽流程獨立於內容來源，尚未自動連結內容中的 Prefab key 或 Icon key。

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
