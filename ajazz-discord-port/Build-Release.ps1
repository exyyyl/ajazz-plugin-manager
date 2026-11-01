[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$nodePath = (Get-Command node -ErrorAction Stop).Source
& $nodePath --test "$PSScriptRoot\compatibility.test.cjs"
if ($LASTEXITCODE -ne 0) { throw 'Compatibility tests failed.' }
& $nodePath --check "$PSScriptRoot\bridge.cjs"
if ($LASTEXITCODE -ne 0) { throw 'Bridge syntax check failed.' }
$releaseRoot = Join-Path $PSScriptRoot 'release'
$stage = Join-Path $PSScriptRoot ('build\release-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$stage\runtime",$releaseRoot -Force | Out-Null
foreach ($file in @('Build-Plugin.ps1','Install-Plugin.ps1','Localize-Profiles.ps1','Launcher.cs','bridge.cjs','compatibility.cjs','ru.json','README.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $stage
}
Copy-Item -LiteralPath $nodePath -Destination "$stage\runtime\node.exe"
Copy-Item -LiteralPath "$PSScriptRoot\..\ajazz-twitch-port\third-party\NODE-LICENSE.txt" -Destination "$stage\runtime\NODE-LICENSE.txt"
$utf8Bom = New-Object Text.UTF8Encoding($true)
foreach ($file in @('Build-Plugin.ps1','Install-Plugin.ps1','Localize-Profiles.ps1')) {
    $full = Join-Path $stage $file
    [IO.File]::WriteAllText($full, [IO.File]::ReadAllText($full), $utf8Bom)
}
[IO.File]::WriteAllText("$stage\Install.cmd", "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0Install-Plugin.ps1`"`r`npause`r`n", [Text.Encoding]::ASCII)
$zip = Join-Path $releaseRoot 'Ajazz-Discord-Installer.zip'
Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal -Force
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$setup = Join-Path $releaseRoot 'Ajazz-Discord-Setup.exe'
& $compiler /nologo /target:exe /optimize+ "/out:$setup" "/resource:$zip,AjazzDiscordInstaller.Payload.zip" /reference:System.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/win32icon:$PSScriptRoot\..\ajazz-manager-electron\resources\AjazzPluginManager.ico" "$PSScriptRoot\setup\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed.' }
& $setup --verify
if ($LASTEXITCODE -ne 0) { throw 'Embedded payload verification failed.' }
Get-FileHash -LiteralPath $setup,$zip -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $([IO.Path]::GetFileName($_.Path))" } | Set-Content "$releaseRoot\SHA256SUMS.txt" -Encoding ascii
Write-Output "Release ready: $releaseRoot"
