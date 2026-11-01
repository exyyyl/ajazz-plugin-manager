$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\Build-Plugin.ps1"
$stage = Join-Path $PSScriptRoot ('build\release-' + [Guid]::NewGuid().ToString('N'))
$target = Join-Path $stage 'dist\com.projectdeck.trula.sdPlugin'
$release = Join-Path $PSScriptRoot 'release'
New-Item -ItemType Directory -Path $target,$release -Force | Out-Null
foreach ($name in @('TrulaPlugin.exe','manifest.json','bridge.js','inspector.js','inspector.html','images','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','WEBVIEW2-LICENSE.txt','WEBVIEW2-NOTICE.txt')) {
    Copy-Item -LiteralPath "$PSScriptRoot\dist\com.projectdeck.trula.sdPlugin\$name" -Destination $target -Recurse
}
$utf8Bom = New-Object Text.UTF8Encoding($true)
[IO.File]::WriteAllText("$stage\Install-Plugin.ps1",[IO.File]::ReadAllText("$PSScriptRoot\Install-Plugin.ps1"),$utf8Bom)
Copy-Item "$PSScriptRoot\README.md" $stage
[IO.File]::WriteAllText("$stage\Install.cmd","@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0Install-Plugin.ps1`"`r`npause`r`n",[Text.Encoding]::ASCII)
$zip = Join-Path $release 'Ajazz-Trula-Installer.zip'
Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal -Force
$setup = Join-Path $release 'Ajazz-Trula-Setup.exe'
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $compiler /nologo /target:exe /optimize+ "/out:$setup" "/resource:$zip,AjazzTrulaInstaller.Payload.zip" /reference:System.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/win32icon:$PSScriptRoot\..\ajazz-manager-electron\resources\AjazzPluginManager.ico" "$PSScriptRoot\setup\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed' }
& $setup --verify
if ($LASTEXITCODE -ne 0) { throw 'Embedded package verification failed' }
Get-FileHash -LiteralPath $setup,$zip -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $([IO.Path]::GetFileName($_.Path))" } | Set-Content "$release\SHA256SUMS.txt" -Encoding ascii
Write-Output "Release ready: $release"
