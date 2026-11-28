#Requires -Version 7
# 文檔健康檢查：Markdown 相對連結都要指向存在的文件，AGENTS.md 要保持精簡（agent 入口，細節留在 specs/）。
# 只檢查倉庫內的 .md，不連網。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $limit = 150
    $agents = @(Get-Content AGENTS.md)
    if ($agents.Count -gt $limit) {
        throw "AGENTS.md 已 $($agents.Count) 行，超過 $limit 行上限；把細節移到 specs/ 或在「任務 → 先讀哪份」加一行指向它"
    }

    $files = @(Get-ChildItem -Recurse -Filter *.md -File |
        Where-Object { $_.FullName -notmatch 'node_modules|\\bin\\|\\obj\\|\\\.tmp' })
    $broken = [System.Collections.Generic.List[string]]::new()
    foreach ($file in $files) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($match in [regex]::Matches($text, '\[[^\]]*\]\(([^)\s]+)\)')) {
            $target = $match.Groups[1].Value
            if ($target -match '^(https?:|mailto:|#)') { continue }
            $path = ($target -split '#')[0]
            if ([string]::IsNullOrWhiteSpace($path)) { continue }
            if (-not (Test-Path -LiteralPath (Join-Path $file.DirectoryName $path))) {
                $broken.Add("$($file.FullName.Replace("$root\", '')) -> $target")
            }
        }
    }
    if ($broken.Count -gt 0) {
        $broken | ForEach-Object { Write-Host "  連結失效: $_" -ForegroundColor Red }
        throw "有 $($broken.Count) 個 Markdown 相對連結指向不存在的文件"
    }

    Write-Host "PASS: $($files.Count) markdown files, links alive, AGENTS.md $($agents.Count)/$limit lines" -ForegroundColor Green
}
finally {
    Pop-Location
}
