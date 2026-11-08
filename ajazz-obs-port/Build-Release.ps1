$ErrorActionPreference = 'Stop'
$stage = Join-Path $PSScriptRoot ('build\importer-' + [Guid]::NewGuid().ToString('N'))
$release = Join-Path $PSScriptRoot 'release'
New-Item -ItemType Directory -Path $stage,$release -Force | Out-Null
try {
    foreach ($file in @('Build-Plugin.ps1','Install-Plugin.ps1','Build-Release.ps1','Install.cmd','actions.json','ru.json','README.md')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $stage
    }
    $archive = Join-Path $release 'Ajazz-OBS-Importer.zip'
    Compress-Archive -Path "$stage\*" -DestinationPath $archive -CompressionLevel Optimal -Force
    Write-Output "Importer archive ready: $archive"
} finally {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    $buildRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'build')).TrimEnd('\') + '\'
    if ($resolvedStage.StartsWith($buildRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedStage)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
