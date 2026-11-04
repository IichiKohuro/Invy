<#
.SYNOPSIS
    Собирает релиз Invy Desktop (Velopack) и кладёт его в каталог релизов сервера Invy.

.DESCRIPTION
    dotnet publish (self-contained, ReadyToRun) → vpk pack → каталог релизов.
    Velopack сам создаёт дельту относительно предыдущей версии, если она лежит в том же каталоге.
    Работает в Windows PowerShell 5.1 и PowerShell 7; на Linux/macOS упаковывает Windows-релиз
    кросс-платформенно (без подписи кода).

.EXAMPLE
    ./tools/pack-desktop.ps1 -Version 0.2.0
    ./tools/pack-desktop.ps1 -Version 0.2.0 -ReleasesDir '\\HOME-PC\Invy\releases\win'
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    # Каталог релизов сервера: {DataPath}\releases\win
    [string] $ReleasesDir,

    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$onWindows = $env:OS -eq 'Windows_NT'

if (-not $ReleasesDir) {
    $ReleasesDir = if ($onWindows) { Join-Path $env:ProgramData 'Invy\releases\win' } else { Join-Path $root '.data/server/releases/win' }
}

$publishDir = Join-Path $root "artifacts/desktop/$Version"
$icon = Join-Path $root 'src/Invy.Desktop/Assets/invy.ico'

function Invoke-Step([string] $title, [scriptblock] $action) {
    Write-Host "==> $title" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) { throw "Шаг «$title» завершился с кодом $LASTEXITCODE" }
}

Invoke-Step 'Восстановление инструментов (vpk)' { dotnet tool restore }

# ReadyToRun ускоряет холодный старт; кросс-компиляция R2R с Linux не поддерживается.
$readyToRun = if ($onWindows) { 'true' } else { 'false' }
Invoke-Step "Публикация $Version ($Runtime)" {
    dotnet publish (Join-Path $root 'src/Invy.Desktop') -c Release -r $Runtime --self-contained true `
        "-p:Version=$Version" "-p:PublishReadyToRun=$readyToRun" -o $publishDir
}

New-Item -ItemType Directory -Force -Path $ReleasesDir | Out-Null
# @(...) обязательно: иначе одноэлементный массив станет строкой и сплаттинг передаст её по символам.
$directive = @(if (-not $onWindows) { '[win]' })
Invoke-Step 'Упаковка Velopack' {
    dotnet vpk @directive pack --packId Invy.Desktop --packVersion $Version --runtime $Runtime `
        --packDir $publishDir --mainExe Invy.Desktop.exe --packTitle Invy --icon $icon --outputDir $ReleasesDir
}

Write-Host "Готово: $ReleasesDir" -ForegroundColor Green
Get-ChildItem $ReleasesDir | Sort-Object LastWriteTime | Select-Object Name, Length | Format-Table -AutoSize
