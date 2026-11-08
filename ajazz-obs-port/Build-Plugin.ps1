[CmdletBinding()]
param(
    [string]$Source = "$env:APPDATA\Elgato\StreamDeck\Plugins\com.elgato.obsstudio.sdPlugin",
    [string]$Destination = "$PSScriptRoot\build\com.elgato.obsstudio.sdPlugin"
)

$ErrorActionPreference = 'Stop'
$pluginId = 'com.elgato.obsstudio'
$translation = Get-Content -LiteralPath "$PSScriptRoot\ru.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$definitions = @(Get-Content -LiteralPath "$PSScriptRoot\actions.json" -Raw -Encoding UTF8 | ConvertFrom-Json)

if (!(Test-Path -LiteralPath "$Source\source\main.html")) {
    throw 'Install the official Elgato OBS Studio plugin first. Its source/main.html file was not found.'
}
if (!(Test-Path -LiteralPath "$Source\resources\icon.png")) {
    throw 'The Elgato OBS plugin resources are incomplete.'
}
$signature = [IO.File]::ReadAllBytes("$Source\manifest.json")
if ($signature.Length -lt 6 -or [Text.Encoding]::ASCII.GetString($signature, 0, 6) -ne 'ELGATO') {
    throw 'The selected folder is not the protected Elgato OBS plugin package.'
}
$english = Get-Content -LiteralPath "$Source\en.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$localizedActions = $translation.Actions
$localizedStrings = $translation.Localization

foreach ($key in $english.Localization.PSObject.Properties.Name) {
    if (!$localizedStrings.PSObject.Properties[$key]) {
        throw "Missing Russian translation for property-inspector text: $key"
    }
}
if ($definitions.Count -ne 19) { throw "Expected 19 OBS actions; found $($definitions.Count)." }
if (Test-Path -LiteralPath $Destination) { throw "Destination already exists: $Destination" }

New-Item -ItemType Directory -Path $Destination -Force | Out-Null
try {
    Copy-Item -LiteralPath "$Source\source" -Destination $Destination -Recurse
    Copy-Item -LiteralPath "$Source\resources" -Destination $Destination -Recurse

    $actions = foreach ($definition in $definitions) {
        $uuid = "$pluginId.$($definition.id)"
        $sourceAction = $english.$uuid
        $localized = $localizedActions.$uuid
        if (!$sourceAction -or !$localized) { throw "Action is missing from the Elgato package or Russian strings: $uuid" }
        if (!(Test-Path -LiteralPath (Join-Path $Destination $definition.inspector))) {
            throw "Property inspector is missing: $($definition.inspector)"
        }
        if ($definition.stateImages.Count -ne $localized.States.Count) {
            throw "State image count for $uuid does not match its Russian state names."
        }

        $states = for ($index = 0; $index -lt $definition.stateImages.Count; $index++) {
            $image = "resources/actions/$($definition.asset)/$($definition.stateImages[$index])"
            $imageFile = Join-Path $Destination ($image + '.png')
            if (!(Test-Path -LiteralPath $imageFile)) {
                $imageFile = Join-Path $Destination ($image + '.svg')
                if (!(Test-Path -LiteralPath $imageFile)) { throw "State artwork is missing: $image" }
            }
            @{
                Name = $localized.States[$index]
                Image = $image
                TitleAlignment = 'bottom'
                FontSize = '12'
            }
        }

        $icon = "resources/actions/$($definition.asset)/icon"
        $iconExists = (Test-Path -LiteralPath (Join-Path $Destination ($icon + '.png'))) -or
            (Test-Path -LiteralPath (Join-Path $Destination ($icon + '.svg')))
        if (!$iconExists) {
            throw "Action icon is missing: $icon"
        }
        @{
            UUID = $uuid
            Name = $localized.Name
            Tooltip = $localized.Tooltip
            Icon = $icon
            States = @($states)
            Controllers = @('Keypad')
            PropertyInspectorPath = $definition.inspector
            SupportedInMultiActions = $true
            UserTitleEnabled = $true
        }
    }

    $manifest = [ordered]@{
        UUID = $pluginId
        Name = $translation.Plugin.Name
        Description = $translation.Plugin.Description
        Author = 'Elgato / AJAZZ import'
        Version = '1.0.0'
        SDKVersion = 1
        CodePath = 'source/main.html'
        CodePathWin = 'source/main.html'
        Icon = 'resources/icon'
        Category = 'OBS Studio'
        CategoryIcon = 'resources/icon'
        PropertyInspectorPath = 'source/main.html'
        Actions = @($actions)
        OS = @(@{ Platform = 'windows'; MinimumVersion = '10' })
        Software = @{ MinimumVersion = '2.10' }
    }
    $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath "$Destination\manifest.json" -Encoding UTF8
    @{ Localization = $translation.Localization } | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath "$Destination\ru.json" -Encoding UTF8

    $streamDeckSource = Join-Path $Destination 'source\streamdeck.js'
    $streamDeckCode = [IO.File]::ReadAllText($streamDeckSource)
    $languagePattern = 'const lang = [^;]*application\.language;'
    if ([regex]::Matches($streamDeckCode, $languagePattern).Count -ne 1) {
        throw 'Unexpected Stream Deck language initialization. Cannot safely force Russian UI.'
    }
    $streamDeckCode = [regex]::Replace($streamDeckCode, $languagePattern, 'const lang = "ru";')
    $messageNeedle = 'let msg = JSON.parseEx(event.data);'
    if ([regex]::Matches($streamDeckCode, [regex]::Escape($messageNeedle)).Count -ne 1) {
        throw 'Unexpected Stream Deck event parser. Cannot safely add AJAZZ event compatibility.'
    }
    $messagePatch = @'
let msg = JSON.parseEx(event.data);
			if (msg && msg.action && msg.action.indexOf("com.elgato.obsstudio.") === 0 && msg.payload && !msg.payload.controller) {
				msg.payload.controller = "Keypad";
			}
'@
    $streamDeckCode = $streamDeckCode.Replace($messageNeedle, $messagePatch.TrimEnd())
    [IO.File]::WriteAllText($streamDeckSource, $streamDeckCode, (New-Object Text.UTF8Encoding($false)))

    $generatedManifest = Get-Content -LiteralPath "$Destination\manifest.json" -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($generatedManifest.Actions.Count -ne 19) { throw 'Generated OBS manifest failed its action-count check.' }
    if ($generatedManifest.CodePathWin -ne 'source/main.html') { throw 'Generated manifest has an unexpected entry point.' }
    Write-Output "Built the Russian AJAZZ OBS plugin with $($generatedManifest.Actions.Count) actions: $Destination"
} catch {
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
    throw
}
