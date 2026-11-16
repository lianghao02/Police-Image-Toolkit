# Agent 協作規則

本專案遵循全域開發憲法與專案根目錄 [AGENTS.md](../AGENTS.md)。

- 平行開發或隔離任務時，依全域憲法以獨立工作區或分支隔離，不讓兩個 Agent 同時操作同一工作目錄。
- 核心測試請執行 `scripts/test.ps1`（勿以 `dotnet test` 作為通過依據）。
- 提交或打包前執行 `scripts/qa.ps1`（或直接執行根目錄 `BUILD.bat` 一鍵完成 QA 與單檔發布）。
