<#
.SYNOPSIS
    Собирает APK Invy для Android и публикует его в каталог релизов сервера вместе с latest.json.

.DESCRIPTION
    dotnet publish (Release, APK) → invy-<версия>.apk + latest.json в {DataPath}\releases\android.
    Все версии должны быть подписаны ОДНИМ ключом, иначе Android откажется ставить обновление.
    Без -KeyStore используется отладочный ключ этого ПК (подходит для проверки на своих устройствах).
    Пароль keystore — в переменной окружения INVY_KEYSTORE_PASS.

.EXAMPLE
    ./tools/pack-android.ps1 -Version 0.2.0
    ./tools/pack-android.ps1 -Version 0.2.0 -KeyStore D:\keys\invy.keystore
    ./tools/pack-android.ps1 -Version 0.2.0 -ApkPath .\my.apk   # без сборки: только публикация готового APK
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    # Каталог релизов сервера: {DataPath}\releases\android
    [string] $ReleasesDir,

    [string] $KeyStore,
    [string] $KeyAlias = 'invy',

    # Готовый APK — пропустить сборку
    [string] $ApkPath,

    [string] $Notes
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$onWindows = $env:OS -eq 'Windows_NT'

if (-not $ReleasesDir) {
    $ReleasesDir = if ($onWindows) { Join-Path $env:ProgramData 'Invy\releases\android' } else { Join-Path $root '.data/server/releases/android' }
}

# versionCode = major*10000 + minor*100 + patch — так же, как в Invy.Android.csproj
$parts = $Version.Split('.') | ForEach-Object { [int]$_ }
$versionCode = $parts[0] * 10000 + $parts[1] * 100 + $parts[2]

if (-not $ApkPath) {
    $publishDir = Join-Path $root "artifacts/android/$Version"
    $arguments = @(
        'publish', (Join-Path $root 'src/Invy.Android'), '-c', 'Release', '-f', 'net10.0-android',
        "-p:VersionPrefix=$Version", '-p:AndroidPackageFormat=apk', '-o', $publishDir
    )
    if ($KeyStore) {
        if (-not $env:INVY_KEYSTORE_PASS) { throw 'Задайте пароль keystore в переменной окружения INVY_KEYSTORE_PASS' }
        $arguments += @(
            '-p:AndroidKeyStore=true', "-p:AndroidSigningKeyStore=$KeyStore", "-p:AndroidSigningKeyAlias=$KeyAlias",
            '-p:AndroidSigningStorePass=env:INVY_KEYSTORE_PASS', '-p:AndroidSigningKeyPass=env:INVY_KEYSTORE_PASS'
        )
    }

    Write-Host "==> Сборка APK $Version (versionCode $versionCode)" -ForegroundColor Cyan
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с кодом $LASTEXITCODE" }

    $ApkPath = Get-ChildItem $publishDir -Filter '*-Signed.apk' | Select-Object -First 1 -ExpandProperty FullName
    if (-not $ApkPath) { throw "Подписанный APK не найден в $publishDir" }
}

New-Item -ItemType Directory -Force -Path $ReleasesDir | Out-Null
$apkName = "invy-$Version.apk"
$target = Join-Path $ReleasesDir $apkName
Copy-Item $ApkPath $target -Force

# Манифест — в формате Invy.Contracts.Updates.AndroidReleaseManifest
$manifest = [ordered]@{
    versionCode = $versionCode
    versionName = $Version
    apkFileName = $apkName
    sha256      = (Get-FileHash $target -Algorithm SHA256).Hash
    size        = (Get-Item $target).Length
    notes       = $Notes
}
$manifestPath = Join-Path $ReleasesDir 'latest.json'
# Сначала во временный файл, затем замена: клиент никогда не прочитает манифест наполовину.
$manifest | ConvertTo-Json | Set-Content -Path "$manifestPath.tmp" -Encoding utf8
Move-Item "$manifestPath.tmp" $manifestPath -Force

Write-Host "Готово: $target" -ForegroundColor Green
Get-Content $manifestPath
