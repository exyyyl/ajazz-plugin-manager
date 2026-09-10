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
$setupSource = Join-Path $projectRoot 'setup\Program.cs'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$node = Join-Path $env:ProgramFiles 'nodejs\node.exe'
$runtimeRoot = Join-Path $pluginRoot 'runtime'
$releaseRoot = Join-Path $projectRoot 'release'
$stagingRoot = Join-Path $releaseRoot 'Ajazz-Twitch-Installer'
$zipPath = Join-Path $releaseRoot 'Ajazz-Twitch-Installer.zip'
$setupPath = Join-Path $releaseRoot 'Ajazz-Twitch-Setup.exe'
$setupIcon = Join-Path (Split-Path -Parent $projectRoot) 'ajazz-manager-electron\resources\AjazzPluginManager.ico'
$publicClientPatch = Join-Path $projectRoot 'patches\Apply-PublicClientRefresh.ps1'
$ircReliabilityPatch = Join-Path $projectRoot 'patches\Apply-IrcReliability.ps1'
$ircLifecyclePatch = Join-Path $projectRoot 'patches\Apply-IrcLifecycle.ps1'
$fastChannelUpdatesPatch = Join-Path $projectRoot 'patches\Apply-FastChannelUpdates.ps1'
$secureLogoutPatch = Join-Path $projectRoot 'patches\Apply-SecureLogout.ps1'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Встроенный компилятор .NET Framework не найден: $compiler"
}
if (-not (Test-Path -LiteralPath $node)) {
    throw "Node.js не найден: $node"
}
if (-not (Test-Path -LiteralPath $setupSource)) {
    throw "Исходник автономного установщика не найден: $setupSource"
}
if (-not (Test-Path -LiteralPath $publicClientPatch)) {
    throw "Рецепт Public Client refresh не найден: $publicClientPatch"
}
if (-not (Test-Path -LiteralPath $ircReliabilityPatch)) {
    throw "Рецепт Twitch IRC reliability не найден: $ircReliabilityPatch"
}
if (-not (Test-Path -LiteralPath $ircLifecyclePatch)) {
    throw "Рецепт Twitch IRC lifecycle не найден: $ircLifecyclePatch"
}
if (-not (Test-Path -LiteralPath $fastChannelUpdatesPatch)) {
    throw "Рецепт быстрых обновлений Twitch-канала не найден: $fastChannelUpdatesPatch"
}
if (-not (Test-Path -LiteralPath $secureLogoutPatch)) {
    throw "Рецепт безопасного выхода Twitch не найден: $secureLogoutPatch"
}

& $publicClientPatch -PluginRoot $pluginRoot
& $ircReliabilityPatch -PluginRoot $pluginRoot
& $ircLifecyclePatch -PluginRoot $pluginRoot
& $fastChannelUpdatesPatch -PluginRoot $pluginRoot
& $secureLogoutPatch -PluginRoot $pluginRoot

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

$stagingInstaller = Join-Path $stagingRoot 'Install-Ajazz-Twitch.ps1'
Copy-Item -LiteralPath (Join-Path $projectRoot 'installer\Install-Ajazz-Twitch.ps1') -Destination $stagingInstaller
# Windows PowerShell 5 treats a UTF-8 script without BOM as the active ANSI
# code page. Preserve Cyrillic strings by writing the distributed script with BOM.
$utf8WithBom = New-Object System.Text.UTF8Encoding($true)
[IO.File]::WriteAllText($stagingInstaller, [IO.File]::ReadAllText($stagingInstaller), $utf8WithBom)
Copy-Item -LiteralPath (Join-Path $projectRoot 'installer\Install.cmd') -Destination $stagingRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $stagingRoot

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal

if (Test-Path -LiteralPath $setupPath) {
    Remove-Item -LiteralPath $setupPath -Force
}
$setupArguments = @(
    '/nologo',
    '/target:exe',
    '/optimize+',
    "/out:$setupPath",
    "/resource:$zipPath,AjazzTwitchInstaller.Payload.zip",
    '/reference:System.dll',
    '/reference:System.IO.Compression.dll',
    '/reference:System.IO.Compression.FileSystem.dll'
)
if (Test-Path -LiteralPath $setupIcon) {
    $setupArguments += "/win32icon:$setupIcon"
}
$setupArguments += $setupSource

Write-Host 'Компилирую однокликовый установщик…'
& $compiler $setupArguments
if ($LASTEXITCODE -ne 0) {
    throw "Сборка Ajazz-Twitch-Setup.exe завершилась с кодом $LASTEXITCODE"
}

Write-Host "Готово: $zipPath" -ForegroundColor Green
Write-Host "Один клик: $setupPath" -ForegroundColor Green
