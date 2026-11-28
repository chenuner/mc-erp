#Requires -Version 7
# 本地一鍵驗證；CI 只做建置打包，不跑檢查。三個 Checks 共用 backend 的 Release 輸出，必須順序執行。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    Write-Host '==> Docs check' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'check-docs.ps1')

    Write-Host '==> dotnet build' -ForegroundColor Cyan
    dotnet build backend/MC.Erp.slnx --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build 失敗' }

    Write-Host '==> Identity checks' -ForegroundColor Cyan
    dotnet run --project tests/Identity.Checks/Identity.Checks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Identity checks 失敗' }

    Write-Host '==> Accounting checks' -ForegroundColor Cyan
    dotnet run --project tests/Accounting.Checks/Accounting.Checks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Accounting checks 失敗' }

    Write-Host '==> Attendance checks' -ForegroundColor Cyan
    dotnet run --project tests/Attendance.Checks/Attendance.Checks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Attendance checks 失敗' }

    Write-Host '==> Frontend crop checks' -ForegroundColor Cyan
    pnpm -C frontend run check
    if ($LASTEXITCODE -ne 0) { throw '前端裁剪檢查失敗' }

    Write-Host '==> Frontend typecheck + build' -ForegroundColor Cyan
    pnpm -C frontend run build
    if ($LASTEXITCODE -ne 0) { throw '前端建置失敗' }

    Write-Host '全部通過' -ForegroundColor Green
}
finally {
    Pop-Location
}
