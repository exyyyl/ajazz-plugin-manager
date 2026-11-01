param(
    [string]$PluginRoot = "$env:APPDATA\HotSpot\StreamDock\plugins",
    [string]$BackupRoot = "$env:APPDATA\AjazzPluginManager\Backups",
    [switch]$NoRestart
)
$ErrorActionPreference = 'Stop'
$destination = Join-Path $PluginRoot 'com.elgato.discord.sdPlugin'
$nodePath = Join-Path $PSScriptRoot 'runtime\node.exe'
if (!(Test-Path -LiteralPath $nodePath)) { $nodePath = (Get-Command node -ErrorAction Stop).Source }
$stageRoot = Join-Path ([IO.Path]::GetTempPath()) ('AjazzDiscord-' + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $stageRoot 'com.elgato.discord.sdPlugin'
$backup = $null
$appPath = $null
try {
    & "$PSScriptRoot\Build-Plugin.ps1" -Destination $package -NodePath $nodePath
    if (Test-Path -LiteralPath $destination) {
        New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
        $backup = Join-Path $BackupRoot ('discord-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
        Copy-Item -LiteralPath $destination -Destination $backup -Recurse
        Write-Output "Backup: $backup"
    }
    $defaultRoot = "$env:APPDATA\HotSpot\StreamDock\plugins"
    if ([IO.Path]::GetFullPath($PluginRoot) -eq [IO.Path]::GetFullPath($defaultRoot)) {
        $app = Get-Process -Name 'Stream Dock AJAZZ' -ErrorAction SilentlyContinue
        if ($app) { $appPath = ($app | Select-Object -First 1).Path; $app | Stop-Process }
    }
    Get-Process -Name ESDDiscord,AjazzDiscord,node -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase) } |
        Stop-Process
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    try {
        # Overlay preserves existing runtime data; original credentials are never packaged.
        Get-ChildItem -LiteralPath $package | Copy-Item -Destination $destination -Recurse -Force
    } catch {
        if ($backup) { Get-ChildItem -LiteralPath $backup | Copy-Item -Destination $destination -Recurse -Force }
        throw
    }
    Write-Output "Installed: $destination"
    Write-Output 'Add a Discord action in AJAZZ, click Request access and approve it in Discord.'
} finally {
    if ($appPath -and !$NoRestart) { Start-Process -FilePath $appPath -WorkingDirectory (Split-Path $appPath) -WindowStyle Hidden }
    $resolvedStage = [IO.Path]::GetFullPath($stageRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolvedStage.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedStage)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
