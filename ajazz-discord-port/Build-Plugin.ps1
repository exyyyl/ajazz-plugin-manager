param(
    [string]$Source = "$env:APPDATA\Elgato\StreamDeck\Plugins\com.elgato.discord.sdPlugin",
    [string]$Destination = "$PSScriptRoot\build\com.elgato.discord.sdPlugin",
    [string]$NodePath = (Get-Command node -ErrorAction SilentlyContinue).Source
)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath "$Source\ESDDiscord.exe")) { throw 'Install the official Discord plugin in Elgato Stream Deck first.' }
if (!$NodePath -or !(Test-Path -LiteralPath $NodePath)) { throw 'Node.js 22+ runtime is required.' }
$nodeVersion = & $NodePath --version
if ($LASTEXITCODE -ne 0 -or $nodeVersion -notmatch '^v(\d+)\.' -or [int]$Matches[1] -lt 22) { throw 'Node.js 22+ runtime is required.' }
if (Test-Path -LiteralPath $Destination) { throw "Destination already exists: $Destination" }
$localization = Get-Content -LiteralPath "$PSScriptRoot\ru.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$definitions = @(
    @('mute','mute','mute_0','mute_1'),
    @('deafen','deafen','deafen_0','deafen_1'),
    @('channel.voice','voiceChannel','voicechannel_0','voicechannel_1'),
    @('channel.text','textChannel','textchannel'),
    @('pushto.talk','pushToTalk','pushtotalk_0','pushtotalk_1'),
    @('pushto.mute','pushToMute','pushtomute_0','pushtomute_1'),
    @('setaudiodevice','setAudioDevice','headphonesAndMicrophone'),
    @('serverstats','serverStats','serverStats'),
    @('pushtotalktoggle','voiceInputModeToggle','ptttoggle_0','ptttoggle_1'),
    @('soundboard','soundboard','soundboard'),
    @('notifications','notifications','notifications'),
    @('uservolumecontrolbutton','userVolumeControl','userVolumeControl'),
    @('volumecontrolbutton','volumeControl','volumeControl'),
    @('videotoggle','toggleCamera','video_0','video_1'),
    @('streamtoggle','toggleScreenShare','stream_0','stream_1')
)
$actions = foreach ($definition in $definitions) {
    $id = "com.elgato.discord.$($definition[0])"
    $entry = $localization.$id
    $stateIndex = 0
    $states = foreach ($image in $definition[2..($definition.Length - 1)]) {
        if (!(Test-Path -LiteralPath "$Source\images\actions\$image.svg")) { throw "Missing image: $image" }
        $state = @{ Image = "images/actions/$image"; TitleAlignment = 'bottom'; FontSize = '12' }
        if ($entry.States -and $stateIndex -lt $entry.States.Count) { $state.Name = $entry.States[$stateIndex].Name }
        $stateIndex++
        $state
    }
    @{
        UUID = $id; Name = $entry.Name; Tooltip = $entry.Tooltip
        Icon = "images/icons/$($definition[1])"; States = @($states)
        Controllers = @('Keypad'); PropertyInspectorPath = 'inspectors/index.html'
        SupportedInMultiActions = $false; UserTitleEnabled = $true
        DisableAutomaticStates = $true
    }
}
$version = (Get-Item -LiteralPath "$Source\ESDDiscord.exe").VersionInfo.FileVersion
if (!$version) { $version = '1.0.0' }
$manifest = @{
    UUID = 'com.elgato.discord'; Name = 'Discord'; Description = $localization.Description
    Author = 'Elgato'; Version = $version; SDKVersion = 2
    CodePath = 'AjazzDiscord.exe'; CodePathWin = 'AjazzDiscord.exe'
    Icon = 'images/plugin'; Category = 'Discord'; CategoryIcon = 'images/category'
    PropertyInspectorPath = 'inspectors/index.html'; Actions = @($actions)
    OS = @(@{ Platform = 'windows'; MinimumVersion = '10' })
    Software = @{ MinimumVersion = '2.9' }
}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
# Copy static resources and executable, never copy runtime credentials or data.
foreach ($name in @('ESDDiscord.exe','z.dll','images','inspectors','Licenses','volume_layout.json')) {
    if (Test-Path -LiteralPath "$Source\$name") { Copy-Item -LiteralPath "$Source\$name" -Destination $Destination -Recurse }
}
foreach ($locale in @('en','de','es','fr','ja','ko','zh_CN','zh_TW')) {
    if (Test-Path -LiteralPath "$Source\$locale.json") { Copy-Item -LiteralPath "$Source\$locale.json" -Destination $Destination }
}
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath "$Destination\manifest.json" -Encoding utf8
Copy-Item -LiteralPath "$PSScriptRoot\ru.json" -Destination $Destination
# This distribution provides Russian UI even when AJAZZ reports another language.
$inspectorMain = Join-Path $Destination 'inspectors\main.js'
$inspectorCode = [IO.File]::ReadAllText($inspectorMain)
$languageSelection = "var language = info['application']['language'];"
if (!$inspectorCode.Contains($languageSelection)) { throw 'Unsupported inspector language initialization. Update the localization patch.' }
$inspectorCode = $inspectorCode.Replace($languageSelection, "var language = 'ru';")
[IO.File]::WriteAllText($inspectorMain, $inspectorCode, (New-Object Text.UTF8Encoding($false)))
Copy-Item -LiteralPath "$PSScriptRoot\bridge.cjs" -Destination $Destination
Copy-Item -LiteralPath "$PSScriptRoot\compatibility.cjs" -Destination $Destination
Copy-Item -LiteralPath $NodePath -Destination "$Destination\node.exe"
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$output = Join-Path ([System.IO.Path]::GetFullPath($Destination)) 'AjazzDiscord.exe'
& $compiler /nologo /target:winexe "/out:$output" "$PSScriptRoot\Launcher.cs"
if ($LASTEXITCODE -ne 0) { throw 'Discord launcher compilation failed' }
Write-Output "Built $Destination ($($actions.Count) keypad actions)"
