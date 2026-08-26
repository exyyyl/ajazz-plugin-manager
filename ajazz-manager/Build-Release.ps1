[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9]{20,64}$')]
    [string]$ClientId = 's8zsg8tto334k3tl1lu3hx71tyr1l6'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$workspaceRoot = Split-Path -Parent $projectRoot
$twitchRoot = Join-Path $workspaceRoot 'ajazz-twitch-port'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$releaseRoot = Join-Path $projectRoot 'release'
$staging = Join-Path $releaseRoot 'Ajazz-Plugin-Manager'
$zipPath = Join-Path $releaseRoot 'Ajazz-Plugin-Manager.zip'
$buildRoot = Join-Path $projectRoot 'build'
$assetsRoot = Join-Path $projectRoot 'assets'
$iconGenerator = Join-Path $buildRoot 'IconGenerator.exe'
$appIcon = Join-Path $assetsRoot 'AjazzPluginManager.ico'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Встроенный компилятор .NET Framework не найден: $compiler"
}
if (-not (Test-Path -LiteralPath (Join-Path $twitchRoot 'Build-Release.ps1'))) {
    throw "Проект Twitch не найден: $twitchRoot"
}

Write-Host 'Подготавливаю проверенный Twitch-пакет…'
& (Join-Path $twitchRoot 'Build-Release.ps1') -ClientId $ClientId
if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать Twitch-пакет.' }

if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
New-Item -ItemType Directory -Path $staging -Force | Out-Null
New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
New-Item -ItemType Directory -Path $assetsRoot -Force | Out-Null

Write-Host 'Создаю иконку приложения…'
& $compiler /nologo /target:exe "/out:$iconGenerator" /reference:System.dll /reference:System.Drawing.dll `
    (Join-Path $projectRoot 'tools\IconGenerator.cs')
if ($LASTEXITCODE -ne 0) { throw 'Не удалось скомпилировать генератор иконки.' }
& $iconGenerator $appIcon
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $appIcon)) { throw 'Не удалось создать иконку приложения.' }

Write-Host 'Компилирую Ajazz Plugin Manager…'
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName
& $compiler /nologo /target:winexe /optimize+ "/win32icon:$appIcon" "/out:$(Join-Path $staging 'AjazzPluginManager.exe')" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Management.dll `
    $sourceFiles
if ($LASTEXITCODE -ne 0) { throw "csc.exe завершился с кодом $LASTEXITCODE" }

$packages = Join-Path $staging 'packages'
New-Item -ItemType Directory -Path $packages -Force | Out-Null
$cleanTwitchPackage = Join-Path $twitchRoot 'release\Ajazz-Twitch-Installer\plugin'
Copy-Item -LiteralPath $cleanTwitchPackage -Destination (Join-Path $packages 'com.elgato.twitch.sdPlugin') -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $staging
Copy-Item -LiteralPath $appIcon -Destination $staging

$selfTestReport = Join-Path $releaseRoot 'self-test.txt'
$selfTest = Start-Process -FilePath (Join-Path $staging 'AjazzPluginManager.exe') `
    -ArgumentList @('--self-test', $selfTestReport) -Wait -PassThru -WindowStyle Hidden
if ($selfTest.ExitCode -ne 0) {
    throw "Самопроверка завершилась с кодом $($selfTest.ExitCode). См. $selfTestReport"
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Готово: $zipPath" -ForegroundColor Green
