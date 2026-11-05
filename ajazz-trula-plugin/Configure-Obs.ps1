param([string]$BackupDirectory)
$ErrorActionPreference = 'Stop'
$obsExecutable = Join-Path $env:ProgramFiles 'obs-studio\bin\64bit\obs64.exe'
if (!(Test-Path -LiteralPath $obsExecutable)) { Write-Output 'OBS shortcut setup skipped: standard OBS installation not found.'; return }
if (!$BackupDirectory) { $BackupDirectory = Join-Path $env:APPDATA ('AjazzPluginManager\Backups\obs-shortcuts-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
$shortcutPaths = @(
    (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'OBS Studio.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'OBS Studio.lnk')
)
# Preserve existing shortcuts, including startup options, and update pinned OBS links.
$scanRoots = @(
    [Environment]::GetFolderPath('DesktopDirectory'),
    [Environment]::GetFolderPath('Programs'),
    (Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar')
)
foreach ($root in $scanRoots) {
    if (!(Test-Path -LiteralPath $root)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $root -Filter '*.lnk' -Recurse -File -ErrorAction SilentlyContinue) {
        $link = $shell.CreateShortcut($file.FullName)
        if ($link.TargetPath -ieq $obsExecutable) { $shortcutPaths += $file.FullName }
    }
}
$changed = 0
foreach ($path in $shortcutPaths | Select-Object -Unique) {
    $exists = Test-Path -LiteralPath $path
    $link = $shell.CreateShortcut($path)
    $original = if ($exists) { [string]$link.Arguments } else { '' }
    $arguments = [regex]::Replace($original, '(?i)(?<!\S)--remote-debugging-(?:port|address)(?:=|\s+)(?:"[^"]*"|\S+)', '').Trim()
    $arguments = ($arguments + ' --remote-debugging-port=17936 --remote-debugging-address=127.0.0.1').Trim()
    if ($exists -and $original -ceq $arguments) { continue }
    if ($exists) { Copy-Item -LiteralPath $path -Destination (Join-Path $BackupDirectory (([Guid]::NewGuid().ToString('N')) + '.lnk')) }
    New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
    $link.TargetPath = $obsExecutable
    $link.WorkingDirectory = Split-Path $obsExecutable
    $link.Arguments = $arguments
    $link.IconLocation = "$obsExecutable,0"
    $link.Description = 'OBS Studio — автоматическое подключение Trula'
    $link.Save()
    $changed++
}
Write-Output "OBS launch shortcuts configured: $changed. Direct exe launches still require the control arguments."
