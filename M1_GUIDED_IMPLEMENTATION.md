# M1 Guided Implementation

本輪完成獨立 Create Content 視窗、欄位、暫存 Draft、基本驗證與文字 Preview 骨架。
沒有建立或寫入內容資產。Renderer Discovery 保留給使用者練習；本文件不代表完整 M1 已完成。

## 既有架構與本輪配置

- 現有 UI 為程式化 UI Toolkit，Editor 類別沒有 namespace；沿用相同方式。
- `ContentDefinition` 是 Runtime 共用定義，`Id` 為 string、`ContentType` 為 int。
- `ContentRecord` 包裝 Definition 與來源；新 Draft 不加入既有來源列表，也不修改 Runtime 模型。
- 新程式位於 `Assets/Game/Editor/ArtContentTool/Creation`，沿用 Editor 目錄隔離方式。
- Draft 的 `ContentType` 仍是 int；UI 使用文字輸入保留空白及格式錯誤，成功解析後才設定 `HasContentType`。沒有自行定義類型值域，0 也有效。
- 驗證重用 `ValidationResult`／`ValidationSeverity`。兩個 ObjectField 僅接受 Project 資產，驗證要求 Prefab 根物件，Source 可使用匯入模型 Prefab。

## 練習資料流與最小修改位置

1. 主視窗 `BuildToolbar()` 的 **Create Content** 按鈕呼叫 `CreateContentWindow.Open()`。
2. `CreateContentWindow.CreateGUI()` 註冊 Source Model ObjectField 的 `RegisterValueChangedCallback`，轉入 `OnSourceModelChanged(GameObject)`。
3. `CreateContentWindow` 的 `[SerializeField] draft` 持有 `ContentCreationDraft`。Draft 保存 Id、ContentType、兩個 Prefab、`Renderers` 清單與 `HasDiscoveredRenderers`。
4. `OnSourceModelChanged` 先清空前次 Discovery 狀態，再對有效 Source 呼叫 `DiscoverSourceRenderers(GameObject)`。
5. **你最少只需要修改 `CreateContentWindow.cs` 中的 `DiscoverSourceRenderers` 方法。** 請自行實作 SkinnedMeshRenderer 搜尋並回傳清單，決定非啟用子物件及巢狀階層的處理方式。只讀取 Source，勿修改或儲存 Prefab。此處刻意沒有提供搜尋 API 或掃描實作。
6. 已預接 `ApplyRendererDiscovery(results)`：把結果寫入 Draft 並設定完成旗標；事件最後透過 `RefreshDraftView()` → `RefreshDraftPreview()` 重新呈現。Preview 僅讀 Draft，不直接掃描 Source。

回傳契約：`null` 表示尚未實作／尚未執行；空清單表示已完成但沒有 Renderer；非空清單表示成功找到結果。不要以空清單冒充尚未執行。

Draft 使用 Unity 序列化保存視窗重載時的狀態；關閉視窗後不提供持久儲存保證。僅修改方法而未更換 Source 時，不會自動重跑 Discovery；編譯完成後請清除再選取 Source。

## 最小 Unity Editor 人工驗證

1. 使用 Unity 6000.3.9f1 開啟此 repository，等待匯入／編譯，確認 Console 沒有新增編譯錯誤。
2. `Tools > Content Tools > Art Content Tool` → **Create Content**。確認開啟獨立視窗，主視窗原有 Content／Art Assets 仍可操作。
3. 初始四個必填欄位應顯示錯誤。Content Type 輸入 `abc` 應顯示整數格式錯誤；輸入 `0` 應通過。Content ID 只輸入空白仍應報錯。
4. 輸入有效 ID，從 Project 指派兩個 Prefab（可用 `Assets/Game/Arts/Pets/Dog/Pet_Dog.prefab` 等既有資產）。確認必填檢查通過，Preview 同步欄位；不會建立新資產。Scene 物件應無法指派。
5. 選取有效 Source，確認 Preview 顯示 `pending guided implementation`，沒有虛假的 Renderer 數量。切換或清除 Source，確認 Preview 立即更新，沒有殘留結果。
6. 自行完成 Discovery 方法並等待編譯後，清除再選 Source：測試有 Renderer 的資產、無 Renderer 的資產及 A → B → 清除。應分別看到正確數量／名稱、0 與待選取提示；沒有前一次結果殘留。
7. 保持視窗開啟並觸發一次腳本重編譯，確認欄位與 Preview 仍一致；回到主視窗測試來源切換、Art Assets 的 Rescan／搜尋／選取。

## 本輪驗證與範圍

已使用本機 Unity 6000.3.9f1 的 Roslyn 編譯器、現有 Editor response file、實際 Unity／套件參考與新增來源，編譯整個 Editor assembly 成功（exit 0、無診斷）。輸出重新導向 `Temp/M1GuidedChecks`，沒有覆蓋 Unity 正在使用的 assembly，也沒有修改產生的 csproj。

此編譯檢查不等同實際 Editor UI 操作；上述人工步驟仍待執行。
未實作 Prefab Variant Creation、Addressables Registration、Export 或 Delete；未執行 commit／push，未存取公司專案。
