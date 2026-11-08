$ErrorActionPreference = 'Stop'
$destination = Join-Path $PSScriptRoot '..\build\validate\com.elgato.obsstudio.sdPlugin'
if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
& "$PSScriptRoot\..\Build-Plugin.ps1" -Destination $destination
$manifest = Get-Content -LiteralPath "$destination\manifest.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$locale = Get-Content -LiteralPath "$destination\ru.json" -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.Actions.Count -ne 19) { throw 'The generated manifest must contain all 19 actions.' }
if ($manifest.Name -ne 'OBS Studio') { throw 'The plugin name was not localized.' }
if (!$manifest.Actions.Where({ $_.Name -eq 'Запись' })) { throw 'The Russian action names are missing.' }
if ($locale.Localization.'OBS Studio is not connected.' -ne 'Нет подключения к OBS Studio.') {
    throw 'The Russian inspector localization map is incomplete.'
}
$required = @('source/main.html','source/main.js','source/streamdeck.js','resources/icon.png')
foreach ($path in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $destination $path))) { throw "Missing imported file: $path" }
}
$streamDeckCode = [IO.File]::ReadAllText("$destination\source\streamdeck.js")
if ($streamDeckCode -notmatch 'const lang = "ru";') { throw 'The importer did not force Russian UI.' }
if ($streamDeckCode -notmatch 'msg\.payload\.controller = "Keypad";') { throw 'The AJAZZ controller compatibility patch is missing.' }
Write-Output 'OBS AJAZZ import build checks passed.'
