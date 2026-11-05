param([string]$PanelUrl, [string]$ProfilePath)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'dist\com.projectdeck.trula.sdPlugin'
if (!(Test-Path -LiteralPath "$source\TrulaPlugin.exe")) { & "$PSScriptRoot\Build-Plugin.ps1" }
$destination = "$env:APPDATA\HotSpot\StreamDock\plugins\com.projectdeck.trula.sdPlugin"
$protectionReady = $false
if (Test-Path -LiteralPath "$destination\TrulaObsGuard.exe") {
    $existingGuard = Start-Process -FilePath "$destination\TrulaObsGuard.exe" -ArgumentList '--check' -WindowStyle Hidden -PassThru -Wait
    $protectionReady = $existingGuard.ExitCode -eq 0
}
if (!$protectionReady -and (Get-Process -Name obs64 -ErrorAction SilentlyContinue)) {
    throw 'Закройте OBS перед установкой защиты Trula и запустите установщик ещё раз. Это завершит старые незащищённые соединения.'
}
$backup = "$env:APPDATA\AjazzPluginManager\Backups\trula-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff')"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$app = Get-Process -Name 'Stream Dock AJAZZ' -ErrorAction SilentlyContinue
$appPath = if ($app) { ($app | Select-Object -First 1).Path } else { $null }
if ($app) { $app | Stop-Process }
Get-Process TrulaPlugin -ErrorAction SilentlyContinue | Where-Object Path -eq "$destination\TrulaPlugin.exe" | Stop-Process
try {
    if (Test-Path -LiteralPath $destination) { Copy-Item -LiteralPath $destination -Destination "$backup\plugin" -Recurse }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($name in @('TrulaPlugin.exe','TrulaObsGuard.exe','manifest.json','obs-player.js','twitch-config.json','inspector.js','inspector.html','images')) {
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination $destination -Recurse -Force
    }
    foreach ($obsolete in @('bridge.js','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','WEBVIEW2-LICENSE.txt','WEBVIEW2-NOTICE.txt','TrulaTests.exe')) {
        $obsoletePath = Join-Path $destination $obsolete
        if (Test-Path -LiteralPath $obsoletePath -PathType Leaf) { Remove-Item -LiteralPath $obsoletePath }
    }
    # Update only the old bundled Trula artwork; preserve custom images and other actions.
    $manifest = Get-Content "$destination\manifest.json" -Raw -Encoding UTF8 | ConvertFrom-Json
    $profilesRoot = "$env:APPDATA\HotSpot\StreamDock\profiles"
    if (Test-Path -LiteralPath $profilesRoot) {
        foreach ($folder in Get-ChildItem -LiteralPath $profilesRoot -Directory -Filter '*.sdProfile') {
            $path = Join-Path $folder.FullName 'manifest.json'
            if (!(Test-Path -LiteralPath $path)) { continue }
            $profile = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
            $changed = $false
            foreach ($property in @($profile.Actions.PSObject.Properties)) {
                $action = $property.Value
                if ($action.UUID -eq 'com.projectdeck.trula.chattest') {
                    $profile.Actions.PSObject.Properties.Remove($property.Name)
                    $changed=$true
                    continue
                }
                if ($action.UUID -in @('com.projectdeck.trula.next','com.projectdeck.trula.previous')) {
                    $selected=if($action.UUID -eq 'com.projectdeck.trula.next'){'nextTrack'}else{'prevTrack'}
                    if (!$action.Settings) {$action.Settings=[PSCustomObject]@{}}
                    $action.Settings | Add-Member -MemberType NoteProperty -Name musicCommand -Value $selected -Force
                    $action.UUID='com.projectdeck.trula.playback'
                    if($action.Name -in @('Предыдущий трек','Следующий трек')) {$action.Name='Управление музыкой'}
                    $changed=$true
                }
                $definition = $manifest.Actions | Where-Object UUID -eq $action.UUID | Select-Object -First 1
                if (!$definition) { continue }
                foreach ($state in $action.States) {
                    if ($state.Image -eq 'images/trula' -and $definition.Icon -ne 'images/trula') { $state.Image=$definition.Icon; $changed=$true }
                }
                if ($action.Name -in @('Подключить Trula','Музыка','Музыка: пуск / пауза','Звук у стримера')) { $action.Name=$definition.Name; $changed=$true }
            }
            if ($changed) {
                New-Item -ItemType Directory -Path "$backup\profiles" -Force | Out-Null
                Copy-Item -LiteralPath $path -Destination (Join-Path "$backup\profiles" ($folder.Name+'.json'))
                $profile | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $path -Encoding UTF8
            }
        }
    }
    if ($PanelUrl) {
        $uri = [Uri]$PanelUrl
        Add-Type -AssemblyName System.Web,System.Security
        if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'trula.io' -or $uri.Port -ne 443 -or $uri.UserInfo) { throw 'Expected https://trula.io/cp/ panel URL.' }
        $token = [Web.HttpUtility]::ParseQueryString($uri.Query)['token']
        $guid = [Guid]::Empty
        if (![Guid]::TryParseExact($token, 'D', [ref]$guid)) { throw 'Invalid panel token' }
        $clear = [Text.Encoding]::UTF8.GetBytes($guid.ToString())
        $encrypted = [Security.Cryptography.ProtectedData]::Protect($clear,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
        New-Item -ItemType Directory "$destination\data" -Force | Out-Null
        [IO.File]::WriteAllBytes("$destination\data\connection.bin",$encrypted)
        [Array]::Clear($clear,0,$clear.Length)
    }
    if ($ProfilePath) {
        $profile = Get-Content -LiteralPath $ProfilePath -Raw -Encoding UTF8 | ConvertFrom-Json
        Copy-Item -LiteralPath $ProfilePath -Destination "$backup\profile.json"
        $manifest = Get-Content "$destination\manifest.json" -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($action in $manifest.Actions) {
            if ($profile.Actions.PSObject.Properties.Value.UUID -contains $action.UUID) { continue }
            $slot = $null
            :findSlot for ($row=0; $row -lt 3; $row++) {
                for ($column=0; $column -lt 5; $column++) {
                    $candidate = "$column,$row"
                    if (!$profile.Actions.PSObject.Properties[$candidate]) { $slot=$candidate; break findSlot }
                }
            }
            if (!$slot) { Write-Output 'No empty slot; add remaining Trula actions manually.'; break }
            $profile.Actions | Add-Member -MemberType NoteProperty -Name $slot -Value ([PSCustomObject]@{
                ActionID=[Guid]::NewGuid().ToString('N'); Controller='Keypad'; Name=$action.Name
                Settings=[PSCustomObject]@{}; State=0; States=$action.States; UUID=$action.UUID
            })
        }
        $profile | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $ProfilePath -Encoding UTF8
    }
    # A cancelled UAC prompt must never enable an unprotected CDP launch.
    try {
        $guard = Join-Path $destination 'TrulaObsGuard.exe'
        $check = Start-Process -FilePath $guard -ArgumentList '--check' -WindowStyle Hidden -PassThru -Wait
        if ($check.ExitCode -ne 0) {
            $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            $setup = Start-Process -FilePath $guard -ArgumentList @('--install',$sid) -Verb RunAs -WindowStyle Hidden -PassThru -Wait
            if ($setup.ExitCode -ne 0) { throw 'Windows OBS protection setup failed.' }
        }
        & "$PSScriptRoot\Configure-Obs.ps1" -BackupDirectory (Join-Path $backup 'obs-shortcuts')
    } catch {
        & "$PSScriptRoot\Configure-Obs.ps1" -BackupDirectory (Join-Path $backup 'obs-shortcuts') -RemoveLegacyOnly
        throw "OBS protection was not configured. Close any OBS still running with old debug arguments. $($_.Exception.Message)"
    }
    Write-Output "Installed Trula. Backup: $backup"
} finally {
    if ($appPath) { Start-Process -FilePath $appPath -WorkingDirectory (Split-Path $appPath) -WindowStyle Hidden }
}
