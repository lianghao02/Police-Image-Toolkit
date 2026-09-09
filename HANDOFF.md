# 當前交接狀態 (Current Handoff)

- **本輪目標**：正式發布 v11.4.0，收斂「輸出追溯索引 `report_index.json`」功能與版本文件，不新增新功能。
- **已完成**：
  - 正式版本同步為 `v11.4.0`（包含 `version.txt`、`PoliceImageToolkit.csproj`、`README.md`、`CHANGELOG.md`、`build.ps1`）。
  - 三個工具的輸出資料夾皆會以原子方式建立或更新 `report_index.json`；索引只保存輸出檔名、來源檔名、工具類型、影片時間（如適用）與像素尺寸。影片截圖復原／刪除時會同步移除對應索引項目；自訂輸出時的「開啟截圖資料夾」會前往實際 `<影片名>_Snapshots` 資料夾。
  - 清理未被引用的歷史單次發布檔（`scripts/release_notes_v11.2.0.md`）。
  - Release Gate 通過：自動化測試 8/8 PASS、QA 檢核 PASS、單檔發布與雜湊驗證 PASS。
- **刻意未修改（保留範圍）**：工具分頁入口、影片解碼與逐格截圖核心、長圖切分演算法、Photo-Report-Generator Repository。
- **驗證結果與測試證據**：`scripts\test.ps1` 與 `scripts\qa.ps1` 皆為 8/8 通過；QA 的 Release 建置 0 警告、0 錯誤。`scripts\build.ps1` 成功建立單檔 EXE，SHA-256 與清單一致。
- **已知事項與注意事項**：Photo-Report-Generator 目前僅讀取自身專案檔，讀取 `report_index.json` 由下游專案另行規劃。HEIC／WebP 本機解碼仰賴 Windows WIC Codec。
- **目前狀態判定**：Stable / Maintenance（P1 = 0，v11.4.0 正式完成發布）
