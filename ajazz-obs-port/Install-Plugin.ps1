[CmdletBinding()]
param(
    [string]$Source = "$env:APPDATA\Elgato\StreamDeck\Plugins\com.elgato.obsstudio.sdPlugin",
    [string]$PluginRoot = "$env:APPDATA\HotSpot\StreamDock\plugins",
    [string]$BackupRoot = "$env:APPDATA\AjazzPluginManager\Backups"
)

$ErrorActionPreference = 'Stop'
$pluginFolder = 'com.elgato.obsstudio.sdPlugin'
$destination = Join-Path $PluginRoot $pluginFolder
$stage = Join-Path ([IO.Path]::GetTempPath()) ('AjazzOBS-' + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $stage $pluginFolder
$appPath = $null
$backup = $null
$installed = $false

try {
    & "$PSScriptRoot\Build-Plugin.ps1" -Source $Source -Destination $package

    $defaultRoot = [IO.Path]::GetFullPath("$env:APPDATA\HotSpot\StreamDock\plugins")
    $requestedRoot = [IO.Path]::GetFullPath($PluginRoot)
    $isDefaultInstall = $requestedRoot.Equals($defaultRoot, [StringComparison]::OrdinalIgnoreCase)
    if ($isDefaultInstall) {
        $app = Get-Process -Name 'Stream Dock AJAZZ' -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($app) {
            $appPath = $app.Path
            [void]$app.CloseMainWindow()
            if (!$app.WaitForExit(10000)) { $app | Stop-Process -Force }
        }
    }

    New-Item -ItemType Directory -Path $PluginRoot -Force | Out-Null
    if (Test-Path -LiteralPath $destination) {
        New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
        $backup = Join-Path $BackupRoot ('obs-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
        Move-Item -LiteralPath $destination -Destination $backup
    }
    try {
        Move-Item -LiteralPath $package -Destination $destination
        $installed = $true
    } catch {
        if ($backup -and (Test-Path -LiteralPath $backup) -and !(Test-Path -LiteralPath $destination)) {
            Move-Item -LiteralPath $backup -Destination $destination
        }
        throw
    }

    Write-Output "Installed Russian OBS Studio actions: $destination"
    if ($backup) { Write-Output "Previous AJAZZ OBS plugin backup: $backup" }
    Write-Output 'The OBS companion plugin from Elgato must stay installed in OBS for its actions to connect.'
} finally {
    if ($appPath -and (Test-Path -LiteralPath $appPath)) {
        Start-Process -FilePath $appPath -WorkingDirectory (Split-Path $appPath) -WindowStyle Normal
    }
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolvedStage.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedStage)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
    if (!$installed -and $backup -and !(Test-Path -LiteralPath $destination) -and (Test-Path -LiteralPath $backup)) {
        Move-Item -LiteralPath $backup -Destination $destination
    }
}
