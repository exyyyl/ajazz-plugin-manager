param([string]$PanelUrl, [string]$ProfilePath)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'dist\com.projectdeck.trula.sdPlugin'
if (!(Test-Path -LiteralPath "$source\TrulaPlugin.exe")) { & "$PSScriptRoot\Build-Plugin.ps1" }
$destination = "$env:APPDATA\HotSpot\StreamDock\plugins\com.projectdeck.trula.sdPlugin"
$backup = "$env:APPDATA\AjazzPluginManager\Backups\trula-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff')"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$app = Get-Process -Name 'Stream Dock AJAZZ' -ErrorAction SilentlyContinue
$appPath = if ($app) { ($app | Select-Object -First 1).Path } else { $null }
if ($app) { $app | Stop-Process }
Get-Process TrulaPlugin -ErrorAction SilentlyContinue | Where-Object Path -eq "$destination\TrulaPlugin.exe" | Stop-Process
try {
    if (Test-Path -LiteralPath $destination) { Copy-Item -LiteralPath $destination -Destination "$backup\plugin" -Recurse }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $source | Copy-Item -Destination $destination -Recurse -Force
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
        foreach ($action in $manifest.Actions | Select-Object -First 3) {
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
    Write-Output "Installed Trula. Backup: $backup"
} finally {
    if ($appPath) { Start-Process -FilePath $appPath -WorkingDirectory (Split-Path $appPath) -WindowStyle Hidden }
}
