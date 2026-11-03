$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dest = Join-Path $root 'dist\com.projectdeck.trula.sdPlugin'
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item -Path (Join-Path $root 'plugin\*') -Destination $dest -Recurse -Force
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Drawing.dll /r:System.Security.dll /r:System.Windows.Forms.dll "/out:$dest\TrulaPlugin.exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
& $compiler /nologo /target:exe /main:TrulaTests /platform:x64 /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Drawing.dll /r:System.Security.dll /r:System.Windows.Forms.dll "/out:$dest\TrulaTests.exe" $sources (Join-Path $root 'tests\TrulaTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& (Join-Path $dest 'TrulaTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
Write-Host "Built: $dest"
