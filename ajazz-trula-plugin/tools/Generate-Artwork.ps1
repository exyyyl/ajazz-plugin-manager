$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$build = Join-Path $root 'build'
New-Item -ItemType Directory -Path $build -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$generator = Join-Path $build 'GenerateArtwork.exe'
& $compiler /nologo /r:System.Drawing.dll "/out:$generator" (Join-Path $root 'src\KeyArtwork.cs') (Join-Path $PSScriptRoot 'GenerateArtwork.cs')
if ($LASTEXITCODE -ne 0) { throw 'Artwork compilation failed' }
& $generator (Join-Path $root 'plugin\images') (Join-Path $build 'key-preview.png')
if ($LASTEXITCODE -ne 0) { throw 'Artwork generation failed' }
