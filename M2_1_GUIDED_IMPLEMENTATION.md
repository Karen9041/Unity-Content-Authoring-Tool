# M2.1 Guided Implementation

M2.1 核心實作與使用者手工驗收已完成，可收尾。核心位於
`Assets/Game/Editor/ArtContentTool/Creation/ContentVariantCreator.cs`
的 `CreateVariantFromTemplate(GameObject baseTemplate, GameObject sourceModel, string outputPath)`。
核心已使用 preview scene 建立 Template / Model instance、保留 Nested Prefab 關係、儲存 Variant，並在 finally 清理暫存物件與關閉 preview scene。

## 已完成的邊界

- Window 保留原有 Draft、Renderer Discovery 與欄位驗證；新增 Create Variant、結果訊息、Ping / Select。
- Content Type 沿用公開資料模型的任意 int（含 0 與負值），不賦予公司領域意義。
- 路徑固定為 `Assets/Game/GeneratedContent/Type_<int>/Content_<trimmed ID>.prefab`。
- ID 去除首尾空白；空白名稱、控制字元、非法檔名字元與結尾句點直接拒絕，不靜默替換。
- Template 僅接受 Regular / Variant asset root；Model 接受 Regular / Variant / Model asset root。
- 拒絕既有檔案、目錄、孤立 .meta、AssetDatabase GUID 及被檔案阻擋的父資料夾。
- Create 在呼叫核心前重新驗證，非只依賴按鈕 enabled 狀態。
- 建立流程回傳 InvalidInput / Failed / Succeeded；NotImplemented 列舉保留但核心已不再使用。例外包含階段、路徑、型別與具體訊息。
- 核心回傳 Succeeded 後才進行 reload 與後置驗證；驗證通過才提供產物給 Ping / Select。
- 本輪不自動刪除任何失敗產物，不修改來源、不 Apply、不 Unpack。

## 已完成的核心工作

核心已由使用者完成，透過 `Create(draft)` 呼叫。以下列出實作責任與驗收基準。
核心本身為低階接點，其前置條件是已通過 Validate，且只使用其產生的 outputPath。

1. 準備兩個局部 GameObject 變數（Template instance / Model instance），初始為 null。
2. 確認父資料夾存在，必要時使用 AssetDatabase.CreateFolder 逐層建立公開輸出資料夾；失敗回報具體路徑。
3. 在 try 內分別用 PrefabUtility.InstantiatePrefab 建立兩個來源的 instance。
4. 讓 Model instance 的 Transform 成為 Template instance 的直接 child，保持 Prefab 連結；明確選擇 local transform 規則。
5. 緊接儲存前再次呼叫 PathOccupied。已有任何輸出就失敗，不呼叫儲存 API；不得用 GenerateUniqueAssetPath 偷換 ID。
6. 呼叫 PrefabUtility.SaveAsPrefabAsset，檢查 out bool 與結果。成功回傳包含 outputPath 的 Succeeded 結果；外層會重新載入驗證。
7. finally 中清理兩個 instance；即使 Model 尚未成功掛入 Template，仍須能獨立清理。清理後不要把暫存 instance 當作 Asset 回傳。

### Owner 與清理責任

| 物件 | Owner | 清理責任 |
|---|---|---|
| 原始 Template / Model asset | AssetDatabase / 使用者 | Creator 不可 Destroy、Apply 或儲存覆寫 |
| Template 暫存 instance | 核心方法 | finally 中 DestroyImmediate |
| Model 暫存 instance | 核心方法 | finally 中 DestroyImmediate；若 Template 已連帶清除它，先用 Unity null 檢查避免重複清理 |
| 已儲存輸出 asset | AssetDatabase / 使用者 | 不自動 Delete；後置驗證失敗時保留現場並顯示原因 |

建立過程使用 EditorSceneManager.NewPreviewScene，兩個 instance 均指定此 scene；核心在 finally 清理 instance 並以 ClosePreviewScene 關閉 scene，避免污染使用者 active scene。
不要使用 DestroyImmediate 的 allowDestroyingAssets=true。

### API 簽名與意義

- `UnityEngine.Object PrefabUtility.InstantiatePrefab(UnityEngine.Object assetComponentOrGameObject, UnityEngine.SceneManagement.Scene destinationScene)`：在指定 preview scene 建立 instance，保留來源 Prefab 連結。
- `void Transform.SetParent(Transform parent, bool worldPositionStays)`：只改 instance 的 parent，決定是否保持 world transform。
- `GameObject PrefabUtility.SaveAsPrefabAsset(GameObject instanceRoot, string assetPath, out bool success)`：輸入須為仍連結的 Template instance root，輸出在 Assets 且副檔名 .prefab。
- `void UnityEngine.Object.DestroyImmediate(UnityEngine.Object obj)`：同步清理 Editor 暫存 instance。

儲存連結中的 Prefab instance root 會產生 Variant；子物件若仍是 Prefab instance，儲存後會成為 Nested Prefab。
官方說明：[SaveAsPrefabAsset](https://docs.unity.com/en-us/engine/7000.0/script-reference/unityeditor/prefabutility/saveasprefabasset)、[InstantiatePrefab](https://docs.unity.com/en-us/engine/6000.5/script-reference/unityeditor/prefabutility/instantiateprefab)。

## 後置驗證

`VerifyCreatedVariant` 檢查檔案存在、AssetDatabase 可載入、GetPrefabAssetType == Variant、
GetCorrespondingObjectFromSource(root) == 選取的 Template。
遍歷包含 inactive 的子物件，要求至少一個 Prefab instance root 的
GetCorrespondingObjectFromSource == 選取的 Model（不能只看同名 child 或 Renderer）。
建立前後另比較 Template / Model dependency hash；這是 imported dependency fingerprint，並非原始檔 byte hash 或磁碟鎖。
人工驗收仍須比較來源檔案及 .meta 的 Git diff；有未儲存的來源編輯時先停止測試。
防覆寫包含 Create 的前置驗證與核心儲存前的第二次檢查；這不提供跨程序的原子鎖保證，請勿同時以其他工具寫入同一路徑。

## 安全檢查與人工驗收

Editor 選單：`Tools > Art Content Tool > Run M2.1 Safe Checks`。
也可透過 executeMethod 呼叫 `ContentVariantCreatorChecks.Run`（僅限未開啟專案的獨立 Editor）。
此為無資產寫入的 Editor smoke checks，未引入 asmdef / NUnit framework 重構。
涵蓋非法 ID、trim、整數 Type 路徑、既有檔案與目錄碰撞、缺欄位，以及無效 draft 不進入核心。
舊的 stub assertion 已移除。這些安全檢查不涵蓋實際 Prefab 儲存，實際流程由下列手工驗收補足。

### 手工驗收基準（使用者已回報成功）

另確認空 ID、非法字元、缺 Template / Model 時按鈕 disabled 並顯示原因。
1. 只用本 repository 的可重用 Template / Model，記錄來源檔案與 .meta hash、Hierarchy 物件數。
2. 用全新 ID 建立；必須通過後置驗證且 Ping / Select 指向磁碟產物。
3. Inspector 確認 Variant base 為 Template；Nested Model 的 source 為原始 Model。不要只憑藍色圖示或名稱判定。
4. 相同 ID 再建一次應拒絕；既有輸出 hash / GUID 不變；輸出路徑被其他檔案或目錄占用亦拒絕。
5. 無效路徑、不可寫目錄或 save 失敗應說明階段與原因；不得顯示 Succeeded。
6. 成功、instantiate 失敗、parenting 前失敗、save 失敗都要確認 finally 清除所有暫存 instance，active scene 沒有額外物件或 dirty 改變。
7. 來源檔案與 .meta hash 不變；Git diff 僅有預期的新輸出，不包含 Template、Model 或 Addressables。

## 完成狀態與驗證紀錄

M2.1 已完成；使用者已回報手工驗收成功。此處區分使用者驗收與 Codex 可直接確認的證據。

- **使用者驗收**：使用者於本輪明確確認手工驗收成功，涵蓋上列實際建立與成功／失敗驗收基準；未另附逐項測試輸出。
- **程式檢查**：核心兩次 InstantiatePrefab、SetParent(..., false)、儲存前碰撞檢查、SaveAsPrefabAsset 的回傳值與 out bool 判斷、finally 清理及後置驗證接線均已具備。
- **UI 檢查**：HelpBox 已先初始化，移除 Not Implemented 提示，保留建立結果與 Ping / Select 入口。
- **產物證據**：repository 已有 Type_1/Content_test01.prefab、Content_test02.prefab 與 Type_2/Content_test03.prefab、Content_test04.prefab。
- **序列化檢查**：Content_test04.prefab 可見 Template 來源連結、Model 來源連結及 child 關係，符合 Variant + Nested Model 結構；此為唯讀檔案證據，不冒充 Editor API 實測。
- **來源變更檢查**：收尾檢查時，Git 變更清單未包含原始 Template / Model 或 Addressables Settings。
- **格式檢查**：git diff --check 通過。
- **編譯證據範圍**：早期輔助架構曾以專案 csproj 參考及本地 Roslyn 編譯通過；後續核心版本未由 Codex 重新編譯，不沿用早期結果當作最新版編譯證明。
- **Editor 操作範圍**：先前 Pipeline 無法連線；Codex 未關閉、重啟或操作 Editor 執行建立。本輪手工驗收結果由使用者提供。
- **版本控制**：所有變更保留未提交；未執行 commit / push。

## 已知限制

- 防覆寫未提供跨程序原子鎖，請勿同時以其他工具寫入同一路徑。
- 來源 dependency hash 是匯入依賴指紋，不等於原始檔案 byte hash。
- EnsureAssetFolderExists 目前以 StartsWith("Assets") 判斷 parent；固定產生的公開輸出路徑不受影響。若未來擴充任意路徑輸入，須精確限制為 Assets 或 Assets/ 子路徑。
- 不自動刪除儲存後驗證失敗的產物，保留現場及具體錯誤。

## M2.2 接點（本輪不實作）

M2.1 已完成並由使用者確認手工驗收成功。下一輪可由已驗證的結果 Asset / OutputPath 接續，
使用既有 draft.Renderers 作為來源候選；須先定義目標 component / 欄位契約與穩定的 Renderer 對應識別方式。
來源 asset 的 Renderer reference 不能直接當作輸出 nested instance 的 serialized reference，必須另做明確對應與驗證。
本輪不加入 component、Binding、Addressables Registration 或 Export。
