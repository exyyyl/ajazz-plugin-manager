param([string]$BackupDirectory, [switch]$RemoveLegacyOnly)
$ErrorActionPreference = 'Stop'
$obsExecutable = Join-Path $env:ProgramFiles 'obs-studio\bin\64bit\obs64.exe'
$pluginDirectory = Join-Path $env:APPDATA 'HotSpot\StreamDock\plugins\com.projectdeck.trula.sdPlugin'
$launcher = Join-Path $pluginDirectory 'TrulaPlugin.exe'
if (!$RemoveLegacyOnly) {
    $check = Start-Process -FilePath (Join-Path $pluginDirectory 'TrulaObsGuard.exe') -ArgumentList '--check' -WindowStyle Hidden -PassThru -Wait
    if ($check.ExitCode -ne 0) { throw 'OBS protection is missing. No debug launch shortcuts were created.' }
}
if (!$BackupDirectory) { $BackupDirectory = Join-Path $env:APPDATA ('AjazzPluginManager\Backups\obs-shortcuts-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
$scanRoots = @(
    [Environment]::GetFolderPath('DesktopDirectory'),
    [Environment]::GetFolderPath('Programs'),
    (Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar')
)
$changed = 0
foreach ($root in $scanRoots) {
    if (!(Test-Path -LiteralPath $root)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $root -Filter '*.lnk' -Recurse -File -ErrorAction SilentlyContinue) {
        $link = $shell.CreateShortcut($file.FullName)
        if ($link.TargetPath -ine $obsExecutable) { continue }
        $original = [string]$link.Arguments
        $arguments = [regex]::Replace($original, '(?i)(?<!\S)--remote-debugging-(?:port|address)(?:=|\s+)(?:"[^"]*"|\S+)', '').Trim()
        # Forward custom profile/collection/scene options as one encoded argument.
        # The launcher parses/requotes them and rejects debug-channel overrides.
        if (!$RemoveLegacyOnly -and (Test-Path -LiteralPath $obsExecutable)) {
            $link.TargetPath = $launcher
            $arguments = if ($arguments) { '--launch-obs ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($arguments)) } else { '--launch-obs' }
            $link.Description = 'OBS Studio — защищённое управление Trula'
        }
        if ($original -ceq $arguments -and $link.TargetPath -ieq $obsExecutable) { continue }
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $BackupDirectory (([Guid]::NewGuid().ToString('N')) + '.lnk'))
        $link.Arguments = $arguments
        $link.IconLocation = "$obsExecutable,0"
        $link.Save()
        $changed++
    }
}
if (!$RemoveLegacyOnly -and (Test-Path -LiteralPath $obsExecutable)) {
    foreach ($folder in @([Environment]::GetFolderPath('DesktopDirectory'),[Environment]::GetFolderPath('Programs'))) {
        $path = Join-Path $folder 'OBS Studio — Trula.lnk'
        if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination (Join-Path $BackupDirectory (([Guid]::NewGuid().ToString('N')) + '.lnk')) }
        $link = $shell.CreateShortcut($path)
        $link.TargetPath = $launcher
        $link.Arguments = '--launch-obs'
        $link.WorkingDirectory = $pluginDirectory
        $link.IconLocation = "$obsExecutable,0"
        $link.Description = 'OBS Studio — защищённое управление Trula'
        $link.Save()
    }
}
Write-Output "OBS shortcuts migrated: $changed. Use OBS Studio — Trula or the plugin launch button."
