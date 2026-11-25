# HANDOFF

## 核心元資料
- **Repository**：lianghao02/Police-Image-Toolkit
- **Branch**：main
- **Commit SHA**：`2275964` 之後之本輪修訂提交（詳見 `git log -1`；承接 `v11.6.0` 與 `2026-11-22` 2D 動漫風格及 `2026-11-25` 品質修訂）
- **Skill Version**：v1.0.0
- **Task Type**：FIX / RELEASE（品質修訂、單檔重新封裝與驗證）
- **Local Path Hint**：03_Police-Image-Toolkit

## 目前狀態
2026-11-25：完成 v11.6.0 唯讀健檢後之 4 項低風險品質修訂（JPG/TIFF 轉檔保留 EXIF 拍攝時間／機型與 ICC 色彩描述檔、縮圖載入可取消防鎖檔、解除多螢幕視窗尺寸鎖定、原圖預覽 Exif 自動轉正與主題統一），並重新封裝本機單檔 `dist/PoliceImageToolkit.exe` 通過實測。

## 已完成與驗證
- 使用 .NET SDK 8.0.406 執行 `scripts/qa.ps1`：Release 建置 **0 警告、0 錯誤**；核心測試 **`27/27`** 通過、來源紀錄整合測試 **`5/5`** 通過；`git diff --check` 與敏感值掃描通過。
- 使用 `scripts/build.ps1` 產出 `win-x64` 自含式單檔 `dist/PoliceImageToolkit.exe`（v11.6.0，大小 `68.64 MB`）。
- 單檔 SHA-256：`bcfcbf622d64ed3134036c662bd50d3da032829d3638d34a2aeea76e33f010ba`。
- 已於本機驗證 `dist/PoliceImageToolkit.exe` 啟動與正常關閉（`DIST_SMOKE_OK=True`、主視窗標題顯示 `手機圖片轉檔與錄影截圖｜Police Image Toolkit v11.6.0`）。

## 本輪核心修訂重點
- **2026-11-25 品質修訂**：
  - `ImageService`：轉檔解碼優先保留 ICC 色彩描述檔（異常時安全退回），JPG／TIFF 輸出保留關鍵 EXIF 拍攝時間與機型並於自動旋轉後將 `Orientation` 歸正為 `1`；新增 `LoadOrientedBitmap` 供預覽共用。
  - `ImageConverterViewModel` / `ImageConverterView`：背景縮圖載入補齊 `CancellationTokenSource` 取消與 `FileShare.ReadWrite | FileShare.Delete`，根除清空清單／測試清理時偶發鎖檔。
  - `MainWindow`：移除 `Loaded` 寫死的 `MaxWidth` / `MaxHeight`，支援筆電拖移至外接大螢幕自由放大。
  - `ImagePreviewWindowHelper`：原圖預覽套用 Exif 自動轉正並統一 2D 日系動漫天青主題資源。
  - `PoliceImageToolkit.CoreTests`：新增 EXIF 保留／原圖預覽旋轉與縮圖取消不鎖檔 2 項回歸測試（`27/27`）。
- **手機錄影截圖工作台**：雙直式「目前影片畫面｜上一張截圖對照」並排（整張／底部 1/3／頂部 1/3／非模態原圖視窗）、補齊 `±0.5 秒`（按鈕及 `Shift + ←/→`）、`1x / 1.5x / 2x` 倍速切換、手機框滾輪 `±0.1 秒`（`Shift` 為 `0.5 秒`）、清單縮圖與點擊預覽、單張移至回收區、多段「切換分段」選單。
- **操作與鍵盤修復**：復原改為 `Ctrl + Z`；修正長按方向鍵焦點跳動、微調後立刻按 `Space` 漏截、輸入框按 `Esc`/`Enter` 恢復快捷鍵，以及 `App.xaml` 全域 `TextBlock` 字色蓋掉 ToolTip／主按鈕白字的問題。
- **鑑識順序、記憶體與輸出潔淨化**：截圖流水號改自既有與回收區最大號之後遞增（不補空號）；還原舊圖依時間與檔名順序插回；頂／底部對照條改為按需單張裁切快取（不擴大常駐記憶體）；`.report_index.lock` 移至 `%LOCALAPPDATA%\PoliceImageToolkit\locks\`；底部新增條件式「開啟／清空回收區」。
- **圖片轉檔與環境防禦**：新增「加入資料夾」鈕、清單縮圖與點擊預覽；記憶上次停留分頁（`LastTabIndex`）；`crash.log` 改寫入 `%LOCALAPPDATA%\PoliceImageToolkit\crash.log`；同步更新 `README.md`、`CHANGELOG.md` 與版本宣告。

## 限制與下一步
- 本機自動化測試不涵蓋完整實片（HEVC/MP4/MOV）長期播放操作與多 DPI（125%/150%）人工視覺驗收；WPF `MediaPlayer` 仍依賴系統可用視訊解碼器。
- 若確認 `RUN.bat` 實測無誤，請再決定是否執行 Git Commit、Push 與推動 `release/v11.6.0` 分支發布。
