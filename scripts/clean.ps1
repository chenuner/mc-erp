#!/usr/bin/env pwsh
# 清理腳本 — 只刪「能重建」或「沒有引用」的東西。
#
#   pwsh -File scripts/clean.ps1                            只掃描，列出每一項佔多少空間，什麼都不刪
#   pwsh -File scripts/clean.ps1 -Apply                     刪掉「基本清理」的項目（臨時目錄、日誌）
#   pwsh -File scripts/clean.ps1 -Apply -Deep               連建置產物一起刪（bin/obj/dist/node_modules）
#   pwsh -File scripts/clean.ps1 -Apply -Deep -KeepNodeModules  保留 node_modules（不想重裝依賴）
#
# 判準：能重新 build / install 出來的（bin、obj、dist、node_modules、logs、.tmp*）才刪；
# 有資料價值的（backend/storage-files、mc-erp-temp、appsettings、.vs）一律不碰。
# 找「零引用的代碼 / 依賴 / 資源」請用 repo-cleanup 技能的 scan.ps1，這支只管磁碟。

[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$Deep,
    [switch]$KeepNodeModules,
    [string]$Root = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 基本清理：臨時目錄、日誌，永遠安全
$Base = @(
    @{ Name = '.tmp*';        Kind = 'Dir';  Why = '會話/工具的臨時目錄' }
    @{ Name = 'backend/logs'; Kind = 'Dir';  Why = '執行日誌，重啟自動重建' }
    @{ Name = '**/*.log';     Kind = 'File'; Why = '零散日誌檔' }
    @{ Name = '**/*.tmp';     Kind = 'File'; Why = '臨時檔' }
)

# 建置產物：-Deep 才刪
$Artifacts = @(
    @{ Name = 'backend/bin';           Kind = 'Dir'; Why = 'dotnet build 產物' }
    @{ Name = 'backend/obj';           Kind = 'Dir'; Why = 'dotnet build 產物' }
    @{ Name = 'tests/*/bin';           Kind = 'Dir'; Why = 'dotnet build 產物' }
    @{ Name = 'tests/*/obj';           Kind = 'Dir'; Why = 'dotnet build 產物' }
    @{ Name = 'frontend/dist';         Kind = 'Dir'; Why = 'vite build 產物' }
    @{ Name = 'frontend/node_modules'; Kind = 'Dir'; Why = 'pnpm install 重新產生' }
)

# 有資料價值 / 下次啟動自動重建：掃描時顯示，永不刪
$Keep = @(
    @{ Name = 'backend/storage-files';     Kind = 'Dir';  Why = '已上傳檔案本體，DB 有引用，刪了縮圖與導出會壞' }
    @{ Name = 'mc-erp-temp';               Kind = 'Dir';  Why = '後端 TMP，執行中的實例在用；重啟自動重建' }
    @{ Name = '.vs';                       Kind = 'Dir';  Why = 'Visual Studio 設定，刪了會重新產生但會丟開窗佈局' }
    @{ Name = 'backend/appsettings*.json'; Kind = 'File'; Why = '設定檔，可能含密鑰' }
)

function Get-EntrySize {
    param([string]$Path, [string]$Kind)
    if (-not (Test-Path -LiteralPath $Path)) { return [int64]0 }
    if ($Kind -eq 'File') {
        return [int64](Get-Item -LiteralPath $Path -Force).Length
    }
    $sum = Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum
    if (-not $sum -or -not $sum.Sum) { return [int64]0 }
    return [int64]$sum.Sum
}

function Resolve-Targets {
    param([hashtable[]]$Specs)
    $result = @()
    foreach ($spec in $Specs) {
        $found = @()
        if ($spec.Kind -eq 'Dir') {
            if ($spec.Name -match '[*?]') {
                $found = @(Get-ChildItem -Path (Join-Path $Root $spec.Name) -Force -ErrorAction SilentlyContinue |
                    Where-Object { $_.PSIsContainer } | ForEach-Object { $_.FullName })
            }
            else {
                $candidate = Join-Path $Root $spec.Name
                if (Test-Path -LiteralPath $candidate -PathType Container) { $found = @($candidate) }
            }
        }
        else {
            $found = @(Get-Item -Path (Join-Path $Root $spec.Name) -Force -ErrorAction SilentlyContinue |
                ForEach-Object { $_.FullName })
        }
        foreach ($path in $found) {
            if (-not $path) { continue }
            if (-not $path.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) { continue }
            if (-not (Test-Path -LiteralPath $path)) { continue }
            $result += [pscustomobject]@{
                Path = $path
                Rel  = $path.Substring($Root.Length).TrimStart('\', '/') -replace '\\', '/'
                Kind = $spec.Kind
                Why  = $spec.Why
                Size = Get-EntrySize -Path $path -Kind $spec.Kind
            }
        }
    }
    return @($result | Sort-Object -Property Rel -Unique)
}

function Format-Size {
    param([int64]$Bytes)
    if ($Bytes -ge 1GB) { return ('{0:N1} GB' -f ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return ('{0:N1} MB' -f ($Bytes / 1MB)) }
    if ($Bytes -ge 1KB) { return ('{0:N0} KB' -f ($Bytes / 1KB)) }
    return "$Bytes B"
}

function Show-Plan {
    param([string]$Title, [object[]]$Items, [string]$Hint)
    Write-Host ""
    Write-Host "== $Title ==" -ForegroundColor Cyan
    if (-not $Items -or $Items.Count -eq 0) {
        Write-Host "  （沒有）" -ForegroundColor DarkGray
        return
    }
    $total = 0
    foreach ($item in $Items) {
        $total += $item.Size
        Write-Host ("  {0,-45} {1,10}" -f $item.Rel, (Format-Size $item.Size))
        Write-Host ("  {0,-45} {1}" -f '', $item.Why) -ForegroundColor DarkGray
    }
    Write-Host ("  合計 {0}  {1}" -f (Format-Size $total), $Hint) -ForegroundColor Yellow
}

function Remove-Items {
    param([object[]]$Items)
    $removed = [int64]0
    $failed = @()
    foreach ($item in $Items) {
        $itemPath = [string]$item.Path
        if ($item.Kind -eq 'File') {
            try {
                Remove-Item -LiteralPath $itemPath -Force -ErrorAction Stop
                $removed += $item.Size
                Write-Host ("  已刪 {0,-42} {1,10}" -f $item.Rel, (Format-Size $item.Size))
            }
            catch {
                $failed += $item.Rel
            }
            continue
        }
        # 資料夾刪除（2026-11-28 實測的三個坑）：
        #   1. 空目錄 / 只剩鎖住檔案的目錄，用 -Force 刪會拋 NullReferenceException，不是 IOException；
        #   2. 有 Deny ACE 的子目錄會拋 UnauthorizedAccessException；
        #   3. 非互動模式下 -Force 失敗會跳確認提示（PSInvalidOperationException）。
        # 所以：先 -Recurse -ErrorAction Stop；失敗才退回逐項刪，並且全程 -Confirm:$false。
        $size = Get-EntrySize -Path $itemPath -Kind 'Dir'
        $blocked = @()
        $why = ''
        try {
            Remove-Item -LiteralPath $itemPath -Recurse -Force -Confirm:$false -ErrorAction Stop
        }
        catch {
            $why = $_.Exception.GetType().Name
            foreach ($child in @(Get-ChildItem -LiteralPath $itemPath -Recurse -File -Force -ErrorAction SilentlyContinue)) {
                if (-not $child) { continue }
                try {
                    Remove-Item -LiteralPath $child.FullName -Force -Confirm:$false -ErrorAction Stop
                }
                catch {
                    $blocked += $child.FullName
                    if (-not $why -or $why -eq 'IOException') { $why = $_.Exception.GetType().Name }
                }
            }
            $children = @(Get-ChildItem -LiteralPath $itemPath -Recurse -Directory -Force -ErrorAction SilentlyContinue |
                Where-Object { $_ } |
                Sort-Object -Property FullName -Descending)
            foreach ($child in $children) {
                if (-not $child) { continue }
                try {
                    Remove-Item -LiteralPath $child.FullName -Force -Confirm:$false -ErrorAction Stop
                }
                catch {
                    # 子目錄裡還有被鎖 / 被拒絕的東西，留著（通常是執行中的程序或 Deny ACE）
                }
            }
            try {
                Remove-Item -LiteralPath $itemPath -Force -Confirm:$false -ErrorAction Stop
            }
            catch {
                # 根目錄裡還有東西，留著
            }
        }
        $removed += $size
        if (-not (Test-Path -LiteralPath $itemPath)) {
            Write-Host ("  已刪 {0,-42} {1,10}" -f $item.Rel, (Format-Size $size))
            if ($blocked.Count -gt 0) {
                Write-Host ("       有 {0} 個檔案沒刪掉（被程序鎖住或目錄拒絕寫入）" -f $blocked.Count) -ForegroundColor Yellow
            }
        }
        else {
            $hint = switch ($why) {
                'IOException' { '被執行中的程序鎖住' }
                'UnauthorizedAccessException' { '目錄上有拒絕（Deny）權限' }
                default { if ($blocked.Count -gt 0) { '部分內容刪不掉' } else { '未知原因' } }
            }
            $failed += ("{0}（{1}）" -f $item.Rel, $hint)
        }
    }
    if ($failed.Count -gt 0) {
        Write-Host ""
        Write-Host "  這些刪不掉（被執行中的程序佔用，或目錄上有拒絕 ACE）：" -ForegroundColor Red
        foreach ($name in $failed) { Write-Host "    - $name" -ForegroundColor Red }
        Write-Host "  被佔用 → 關掉 dotnet / vite / Visual Studio 後重跑；ACE 拒絕 → 要管理員執行 icacls 重置。" -ForegroundColor DarkGray
    }
    return [int64]$removed
}

# ---- 主流程 ----
if (-not $Root) {
    $Root = Split-Path -Parent $PSScriptRoot
    if (-not (Test-Path -LiteralPath (Join-Path $Root 'backend/MC.Erp.slnx'))) {
        $Root = (Get-Location).Path
    }
}
$Root = (Resolve-Path -LiteralPath $Root).Path

$baseItems = Resolve-Targets -Specs $Base

# tests/*/bin 這種通配要先展開成具體專案
$artifactSpecs = @()
foreach ($spec in $Artifacts) {
    if ($spec.Name -like 'tests/*') {
        $leaf = ($spec.Name -split '/')[-1]
        foreach ($dir in Get-ChildItem -LiteralPath (Join-Path $Root 'tests') -Directory -Force -ErrorAction SilentlyContinue) {
            if (Test-Path -LiteralPath (Join-Path $dir.FullName $leaf)) {
                $artifactSpecs += @{ Name = "tests/$($dir.Name)/$leaf"; Kind = 'Dir'; Why = $spec.Why }
            }
        }
    }
    else {
        $artifactSpecs += $spec
    }
}
$artifactItems = Resolve-Targets -Specs $artifactSpecs
if ($KeepNodeModules) {
    $artifactItems = @($artifactItems | Where-Object { $_.Rel -ne 'frontend/node_modules' })
}
$keepItems = Resolve-Targets -Specs $Keep

Write-Host "工作區：$Root"
Write-Host ("模式：{0}" -f $(if ($Apply) { 'APPLY（會真的刪）' } else { 'DRY RUN（只列不刪，加 -Apply 才刪）' })) -ForegroundColor Green

Show-Plan -Title '基本清理（-Apply 時刪）' -Items $baseItems -Hint ''
if ($Deep -or -not $Apply) {
    Show-Plan -Title '建置產物（-Deep 才刪，可重建）' -Items $artifactItems -Hint '下一次 dotnet build / pnpm install 重建'
}
Show-Plan -Title '保留不動（只顯示）' -Items $keepItems -Hint '有資料價值，不刪'

if (-not $Apply) {
    Write-Host ""
    Write-Host "確認要刪就加 -Apply；連建置產物一起刪就加 -Apply -Deep。" -ForegroundColor Cyan
    exit 0
}

$targets = @($baseItems)
if ($Deep) { $targets += $artifactItems }
$targets = @($targets | Where-Object { $_.Size -gt 0 -or $_.Kind -eq 'File' })

if ($targets.Count -eq 0) {
    Write-Host ""
    Write-Host "沒有可清理的項目。" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "開始刪除…" -ForegroundColor Cyan
$freed = Remove-Items -Items $targets
Write-Host ""
Write-Host ("共釋放 {0}" -f (Format-Size $freed)) -ForegroundColor Green
Write-Host "刪完記得跑一次驗證：pwsh -File scripts/check.ps1" -ForegroundColor DarkGray
