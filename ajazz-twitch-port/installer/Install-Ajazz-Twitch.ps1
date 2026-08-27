[CmdletBinding()]
param(
    [string]$ClientId,
    [string]$PluginSource = (Join-Path $PSScriptRoot 'plugin')
)

$ErrorActionPreference = 'Stop'
$pluginName = 'com.elgato.twitch.sdPlugin'
$streamDockRoot = Join-Path $env:APPDATA 'HotSpot\StreamDock'
$pluginsRoot = Join-Path $streamDockRoot 'plugins'
$target = Join-Path $pluginsRoot $pluginName
$source = [IO.Path]::GetFullPath($PluginSource)
$targetFull = [IO.Path]::GetFullPath($target)
$pluginsRootFull = [IO.Path]::GetFullPath($pluginsRoot) + [IO.Path]::DirectorySeparatorChar

function Remove-BackupCredentials {
    param([Parameter(Mandatory = $true)][string]$BackupRoot)

    if (-not (Test-Path -LiteralPath $BackupRoot)) {
        return 0
    }

    $backupRootFull = [IO.Path]::GetFullPath($BackupRoot) + [IO.Path]::DirectorySeparatorChar
    $removed = 0
    Get-ChildItem -LiteralPath $BackupRoot -Directory -Filter "$pluginName-*" -ErrorAction SilentlyContinue |
        ForEach-Object {
            $backupDirectory = $_.FullName
            foreach ($fileName in @('auth.bin', 'auth.bin.bak', 'auth.bin.tmp')) {
                $candidate = [IO.Path]::GetFullPath((Join-Path $backupDirectory "data\$fileName"))
                if (-not $candidate.StartsWith($backupRootFull, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Unsafe backup credential path: $candidate"
                }
                if (Test-Path -LiteralPath $candidate) {
                    Remove-Item -LiteralPath $candidate -Force
                    $removed++
                }
            }
        }
    return $removed
}

if (-not $targetFull.StartsWith($pluginsRootFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe plugin target: $targetFull"
}

if (-not (Test-Path -LiteralPath (Join-Path $source 'manifest.json'))) {
    throw "Plugin package not found: $source"
}

if ([string]::IsNullOrWhiteSpace($ClientId)) {
    $packagedConfig = Get-Content -LiteralPath (Join-Path $source 'auth-config.json') -Raw | ConvertFrom-Json
    $ClientId = [string]$packagedConfig.clientId
}

if ([string]::IsNullOrWhiteSpace($ClientId) -or $ClientId -eq 'PASTE_PUBLIC_TWITCH_CLIENT_ID_HERE') {
    $ClientId = Read-Host 'Введите публичный Twitch Client ID'
}

if ($ClientId -notmatch '^[A-Za-z0-9]{20,64}$') {
    throw 'Twitch Client ID должен состоять из 20–64 латинских букв и цифр.'
}

$ajazzCandidates = @(@(
    (Join-Path ${env:ProgramFiles(x86)} 'HotSpot\Stream Dock AJAZZ.exe'),
    (Join-Path $env:ProgramFiles 'HotSpot\Stream Dock AJAZZ.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) })

if ($ajazzCandidates.Count -eq 0) {
    throw 'Stream Dock AJAZZ не найден. Сначала установите приложение Ajazz.'
}

Write-Host 'Останавливаю Stream Dock AJAZZ…'
Get-Process -Name 'Stream Dock AJAZZ' -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name 'TwitchLauncher' -ErrorAction SilentlyContinue | Stop-Process -Force
Get-CimInstance Win32_Process -Filter "Name = 'node.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like '*com.elgato.twitch.sdPlugin*bin*plugin.js*' } |
    ForEach-Object { Invoke-CimMethod -InputObject $_ -MethodName Terminate | Out-Null }
Start-Sleep -Milliseconds 800

New-Item -ItemType Directory -Path $pluginsRoot -Force | Out-Null
$preserveCredential = $false
$preservedCredentialHash = $null
if (Test-Path -LiteralPath $targetFull) {
    $oldCredential = Join-Path $targetFull 'data\auth.bin'
    $oldConfig = Join-Path $targetFull 'auth-config.json'
    if ((Test-Path -LiteralPath $oldCredential) -and (Test-Path -LiteralPath $oldConfig)) {
        try {
            $oldClientId = [string](Get-Content -LiteralPath $oldConfig -Raw | ConvertFrom-Json).clientId
            $preserveCredential = $oldClientId -eq $ClientId
            if ($preserveCredential) {
                $preservedCredentialHash = (Get-FileHash -LiteralPath $oldCredential -Algorithm SHA256).Hash
            }
        }
        catch {
            $preserveCredential = $false
        }
    }
    $backupRoot = Join-Path $streamDockRoot 'plugin-backups'
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $backup = Join-Path $backupRoot ($pluginName + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Move-Item -LiteralPath $targetFull -Destination $backup
    Write-Host "Предыдущая версия сохранена: $backup"
}

Write-Host 'Устанавливаю Twitch plugin…'
Copy-Item -LiteralPath $source -Destination $targetFull -Recurse
$installedData = Join-Path $targetFull 'data'
if (Test-Path -LiteralPath $installedData) {
    Remove-Item -LiteralPath $installedData -Recurse -Force
}
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$authConfigJson = @{ clientId = $ClientId } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $targetFull 'auth-config.json'), $authConfigJson, $utf8NoBom)

if ($preserveCredential) {
    New-Item -ItemType Directory -Path $installedData -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $backup 'data\auth.bin') -Destination (Join-Path $installedData 'auth.bin')
}
else {
    New-Item -ItemType File -Path (Join-Path $targetFull 'first-run-auth') -Force | Out-Null
}

$manifest = Get-Content -LiteralPath (Join-Path $targetFull 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.Actions.Count -ne 16) {
    throw "Проверка установки не пройдена: ожидалось 16 действий, найдено $($manifest.Actions.Count)."
}
if ($preserveCredential) {
    $installedCredential = Join-Path $installedData 'auth.bin'
    if (-not (Test-Path -LiteralPath $installedCredential) -or
        (Get-FileHash -LiteralPath $installedCredential -Algorithm SHA256).Hash -ne $preservedCredentialHash) {
        throw 'Проверка установки не пройдена: защищённая Twitch-сессия не была перенесена.'
    }
}

$removedBackupCredentials = Remove-BackupCredentials -BackupRoot (Join-Path $streamDockRoot 'plugin-backups')
if ($removedBackupCredentials -gt 0) {
    Write-Host "Удалены защищённые auth.bin из резервных копий: $removedBackupCredentials"
}

Write-Host 'Запускаю Stream Dock AJAZZ…'
$ajazzExe = $ajazzCandidates[0]
Start-Process -FilePath $ajazzExe -WorkingDirectory (Split-Path -Parent $ajazzExe)
Write-Host ''
if ($preserveCredential) {
    Write-Host 'Готово. Существующая авторизация Twitch сохранена.' -ForegroundColor Green
}
else {
    Write-Host 'Готово. Подтвердите доступ на автоматически открывшейся странице Twitch.' -ForegroundColor Green
}
