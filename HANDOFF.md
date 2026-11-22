# HANDOFF

## 核心元資料
- **Repository**：lianghao02/Police-Image-Toolkit
- **Branch**：main
- **Commit SHA**：`4481151`（承接 `v11.6.0` 發布提交並包含 `2026-11-22` 介面視覺調校與 `RUN-DEV.bat`）
- **Skill Version**：v1.0.0
- **Task Type**：IMPROVE / RELEASE（本機建置與驗證）
- **Local Path Hint**：03_Police-Image-Toolkit

## 目前狀態
2026-11-22：v11.6.0 與 `2026-11-22` 介面調校（`11f3d51` 深警務藍標題列與降眩光冷灰藍分層色調、`4481151` 2D 日系動漫天青硬邊風格與 `RUN-DEV.bat`）皆已提交至 `main`；本機單檔 v11.6.0 建置與自動化測試（`25/25` 核心、`5/5` 整合）記錄如下。Windows 實片長期播放、多 DPI（125%/150%）視覺 QA 與是否重新封裝發版仍待實機確認。

## 已完成與驗證
- 使用 .NET SDK 8.0.406 執行 `scripts/qa.ps1`：Release 建置 **0 警告、0 錯誤**；核心測試 **`25/25`** 通過、來源紀錄整合測試 **`5/5`** 通過；`git diff --check` 與敏感值掃描通過。
- 使用 `scripts/build.ps1` 產出 `win-x64` 自含式單檔 `dist/PoliceImageToolkit.exe`（v11.6.0，大小 `68.64 MB`）。
- 單檔 SHA-256：`f7135d21ac8ab44500e67c6370c8986265531fd4e95a988b2ffd75618550d27c`。
- 已於本機驗證 `dist/PoliceImageToolkit.exe` 啟動與正常關閉（`Responding=True`、主視窗標題顯示 `v11.6.0`、`ExitCode=0`）。

## 本輪核心修訂重點
- **手機錄影截圖工作台**：雙直式「目前影片畫面｜上一張截圖對照」並排（整張／底部 1/3／頂部 1/3／非模態原圖視窗）、補齊 `±0.5 秒`（按鈕及 `Shift + ←/→`）、`1x / 1.5x / 2x` 倍速切換、手機框滾輪 `±0.1 秒`（`Shift` 為 `0.5 秒`）、清單縮圖與點擊預覽、單張移至回收區、多段「切換分段」選單。
- **操作與鍵盤修復**：復原改為 `Ctrl + Z`；修正長按方向鍵焦點跳動、微調後立刻按 `Space` 漏截、輸入框按 `Esc`/`Enter` 恢復快捷鍵，以及 `App.xaml` 全域 `TextBlock` 字色蓋掉 ToolTip／主按鈕白字的問題。
- **鑑識順序、記憶體與輸出潔淨化**：截圖流水號改自既有與回收區最大號之後遞增（不補空號）；還原舊圖依時間與檔名順序插回；頂／底部對照條改為按需單張裁切快取（不擴大常駐記憶體）；`.report_index.lock` 移至 `%LOCALAPPDATA%\PoliceImageToolkit\locks\`；底部新增條件式「開啟／清空回收區」。
- **圖片轉檔與環境防禦**：新增「加入資料夾」鈕、清單縮圖與點擊預覽；記憶上次停留分頁（`LastTabIndex`）；`crash.log` 改寫入 `%LOCALAPPDATA%\PoliceImageToolkit\crash.log`；同步更新 `README.md`、`CHANGELOG.md` 與版本宣告。

## 限制與下一步
- 本機自動化測試不涵蓋完整實片（HEVC/MP4/MOV）長期播放操作與多 DPI（125%/150%）人工視覺驗收；WPF `MediaPlayer` 仍依賴系統可用視訊解碼器。
- 若確認 `RUN.bat` 實測無誤，請再決定是否執行 Git Commit、Push 與推動 `release/v11.6.0` 分支發布。
