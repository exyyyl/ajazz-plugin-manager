[CmdletBinding()]
param(
    [string]$PluginRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'com.elgato.twitch.sdPlugin')
)

$ErrorActionPreference = 'Stop'
$pluginScript = Join-Path $PluginRoot 'bin\plugin.js'
if (-not (Test-Path -LiteralPath $pluginScript)) {
    throw "Twitch plugin.js not found: $pluginScript"
}

$content = [IO.File]::ReadAllText($pluginScript)
if ($content.Contains('This Twitch Client ID is Confidential.') -and
    $content.Contains('const verifiedCredential = await refreshAjazzCredential(credential);')) {
    Write-Host 'Public-client refresh patch is already applied.'
    return
}

$usesCrLf = $content.Contains("`r`n")
$normalized = $content.Replace("`r`n", "`n")

$oldRefresh = @'
    const { result } = await twitchFormRequest("https://id.twitch.tv/oauth2/token", {
        client_id: clientId,
        grant_type: "refresh_token",
        refresh_token: credential.refreshToken,
    });
'@
$newRefresh = @'
    let result;
    try {
        ({ result } = await twitchFormRequest("https://id.twitch.tv/oauth2/token", {
            client_id: clientId,
            grant_type: "refresh_token",
            refresh_token: credential.refreshToken,
        }));
    }
    catch (error) {
        if (String(error?.message || error).toLowerCase().includes("client secret")) {
            throw new Error("This Twitch Client ID is Confidential. Create a Public Twitch application for AJAZZ so tokens can refresh without a Client Secret.");
        }
        throw error;
    }
'@
$oldSave = @'
    await saveAjazzCredential(credential);
    streamDeck.settings.getGlobalSettings();
    return credential;
'@
$newSave = @'
    const verifiedCredential = await refreshAjazzCredential(credential);
    streamDeck.settings.getGlobalSettings();
    return verifiedCredential;
'@

if (-not $normalized.Contains($oldRefresh)) {
    throw 'Refresh-token block was not found. The upstream Twitch plugin version may have changed.'
}
if (-not $normalized.Contains($oldSave)) {
    throw 'Device-authorization save block was not found. The upstream Twitch plugin version may have changed.'
}

$normalized = $normalized.Replace($oldRefresh, $newRefresh).Replace($oldSave, $newSave)
if ($usesCrLf) {
    $normalized = $normalized.Replace("`n", "`r`n")
}
[IO.File]::WriteAllText($pluginScript, $normalized, (New-Object System.Text.UTF8Encoding($false)))
Write-Host 'Applied Public-client refresh verification patch.' -ForegroundColor Green
