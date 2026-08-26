[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9]{20,64}$')]
    [string]$ClientId
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$pluginRoot = Join-Path $projectRoot 'com.elgato.twitch.sdPlugin'
$launcherSource = Join-Path $projectRoot 'launcher\Program.cs'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$node = Join-Path $env:ProgramFiles 'nodejs\node.exe'
$runtimeRoot = Join-Path $pluginRoot 'runtime'
$releaseRoot = Join-Path $projectRoot 'release'
$stagingRoot = Join-Path $releaseRoot 'Ajazz-Twitch-Installer'
$zipPath = Join-Path $releaseRoot 'Ajazz-Twitch-Installer.zip'
$publicClientPatch = Join-Path $projectRoot 'patches\Apply-PublicClientRefresh.ps1'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Встроенный компилятор .NET Framework не найден: $compiler"
}
if (-not (Test-Path -LiteralPath $node)) {
    throw "Node.js не найден: $node"
}
if (-not (Test-Path -LiteralPath $publicClientPatch)) {
    throw "Рецепт Public Client refresh не найден: $publicClientPatch"
}

& $publicClientPatch -PluginRoot $pluginRoot

Write-Host 'Компилирую автономный загрузчик…'
& $compiler /nologo /target:exe "/out:$(Join-Path $pluginRoot 'TwitchLauncher.exe')" /reference:System.dll /reference:System.Security.dll $launcherSource
if ($LASTEXITCODE -ne 0) {
    throw "csc.exe завершился с кодом $LASTEXITCODE"
}

New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
Copy-Item -LiteralPath $node -Destination (Join-Path $runtimeRoot 'node.exe') -Force
if (Test-Path -LiteralPath (Join-Path $projectRoot 'third-party\NODE-LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'third-party\NODE-LICENSE.txt') -Destination (Join-Path $runtimeRoot 'NODE-LICENSE.txt') -Force
}

@(
    'TwitchLauncher.deps.json',
    'TwitchLauncher.dll',
    'TwitchLauncher.runtimeconfig.json',
    'TwitchLauncher.test.exe'
) | ForEach-Object {
    $obsolete = Join-Path $pluginRoot $_
    if (Test-Path -LiteralPath $obsolete) {
        Remove-Item -LiteralPath $obsolete -Force
    }
}

& $node --check (Join-Path $pluginRoot 'bin\plugin.js')
if ($LASTEXITCODE -ne 0) {
    throw 'Проверка JavaScript завершилась с ошибкой.'
}

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
$stagingPlugin = Join-Path $stagingRoot 'plugin'
Copy-Item -LiteralPath $pluginRoot -Destination $stagingPlugin -Recurse

@('data', 'logs') | ForEach-Object {
    $privatePath = Join-Path $stagingPlugin $_
    if (Test-Path -LiteralPath $privatePath) {
        Remove-Item -LiteralPath $privatePath -Recurse -Force
    }
}
$launcherLog = Join-Path $stagingPlugin 'launcher-error.log'
if (Test-Path -LiteralPath $launcherLog) {
    Remove-Item -LiteralPath $launcherLog -Force
}
$firstRunMarker = Join-Path $stagingPlugin 'first-run-auth'
if (Test-Path -LiteralPath $firstRunMarker) {
    Remove-Item -LiteralPath $firstRunMarker -Force
}
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$authConfigJson = @{ clientId = $ClientId } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $stagingPlugin 'auth-config.json'), $authConfigJson, $utf8NoBom)

Copy-Item -LiteralPath (Join-Path $projectRoot 'installer\Install-Ajazz-Twitch.ps1') -Destination $stagingRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'installer\Install.cmd') -Destination $stagingRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $stagingRoot

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Готово: $zipPath" -ForegroundColor Green
