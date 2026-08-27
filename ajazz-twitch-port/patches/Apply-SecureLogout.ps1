[CmdletBinding()]
param(
    [string]$PluginRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'com.elgato.twitch.sdPlugin')
)

$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$pluginScript = Join-Path $PluginRoot 'bin\plugin.js'
$sharedScript = Join-Path $PluginRoot 'ui\property-inspector\shared.js'

if (-not (Test-Path -LiteralPath $pluginScript) -or -not (Test-Path -LiteralPath $sharedScript)) {
    throw "Twitch plugin files were not found below: $PluginRoot"
}

$content = [IO.File]::ReadAllText($pluginScript).Replace("`r`n", "`n")
if (-not $content.Contains('async function revokeAjazzCredential()')) {
    $old = @'
async function saveAjazzCredential(credential) {
    await runCredentialHelper("--credential-write", JSON.stringify(credential));
}
'@
    $new = @'
async function saveAjazzCredential(credential) {
    await runCredentialHelper("--credential-write", JSON.stringify(credential));
}

async function deleteAjazzCredential() {
    await runCredentialHelper("--credential-delete");
}

async function revokeAjazzCredential() {
    const credential = await loadAjazzCredential();
    if (credential?.accessToken && credential?.clientId) {
        const response = await twitchFormRequest("https://id.twitch.tv/oauth2/revoke", {
            client_id: credential.clientId,
            token: credential.accessToken,
        }, true);
        if (!response.ok)
            throw new Error(response.result.message || `Twitch token revocation returned HTTP ${response.status}.`);
    }
    await deleteAjazzCredential();
    ajazzValidatedThisProcess = false;
    for (const socket of pluginStore.ircSockets.values()) {
        socket.availableRetries = 0;
        socket.websocket?.close();
    }
    for (const socket of pluginStore.websockets.values()) {
        socket.availableRetries = 0;
        socket.websocket?.close();
        socket.expiringWebsocket?.close();
    }
    pluginStore.ircSockets.clear();
    pluginStore.websockets.clear();
    pluginStore.accounts.clear();
    pluginEventEmitter.emitAccountsChanged(pluginStore.accounts);
}
'@
    if (-not $content.Contains($old)) {
        throw 'Credential save block was not found. The AJAZZ auth bridge may have changed.'
    }
    $content = $content.Replace($old, $new)

    $old = @'
    if (ev.payload?.event !== "ajazzAuthStart")
        return;
'@
    $new = @'
    if (ev.payload?.event === "ajazzAuthLogout") {
        revokeAjazzCredential()
            .then(async () => {
            sendAccountsToPropertyInspector(ev);
            await connection.send({ event: "showOk", context: ev.context });
        })
            .catch(async (error) => {
            streamDeck.logger.error("Unable to revoke the Ajazz Twitch login.", error);
            await connection.send({ event: "showAlert", context: ev.context });
        });
        return;
    }
    if (ev.payload?.event !== "ajazzAuthStart")
        return;
'@
    if (-not $content.Contains($old)) {
        throw 'AJAZZ sendToPlugin auth block was not found.'
    }
    $content = $content.Replace($old, $new)

    $old = @'
        if (credential) {
            settings.accounts = [{
                    uniqueIdentifier: credential.userId,
                    displayName: credential.displayName || credential.login,
                    token: credential.accessToken,
                }];
        }
'@
    $new = @'
        if (credential) {
            settings.accounts = [{
                    uniqueIdentifier: credential.userId,
                    displayName: credential.displayName || credential.login,
                    token: credential.accessToken,
                }];
        }
        else {
            // AJAZZ may retain old Elgato global settings. A missing protected
            // credential is authoritative after an explicit logout.
            settings.accounts = [];
        }
'@
    if (-not $content.Contains($old)) {
        throw 'AJAZZ credential restore block was not found.'
    }
    $content = $content.Replace($old, $new)
    [IO.File]::WriteAllText($pluginScript, $content, $utf8NoBom)
}

$shared = [IO.File]::ReadAllText($sharedScript).Replace("`r`n", "`n")
if (-not $shared.Contains('id="ajazz-twitch-logout"')) {
    $anchor = 'document.head.appendChild(ajazzContrastStyles);'
    $addition = @'
document.head.appendChild(ajazzContrastStyles);

const logoutItem = document.createElement("div");
logoutItem.className = "sdpi-item";
logoutItem.style.marginTop = "4px";
logoutItem.innerHTML = `
	<div class="sdpi-item-label"></div>
	<button type="button" id="ajazz-twitch-logout" class="sdpi-item-value" style="min-height: 28px; cursor: pointer;">
		Выйти из Twitch
	</button>
`;
accountSelect.closest(".sdpi-item")?.insertAdjacentElement("afterend", logoutItem);
const logoutButton = document.getElementById("ajazz-twitch-logout");
logoutButton?.addEventListener("click", () => {
	if (!window.confirm("Выйти из Twitch на этом компьютере и отозвать токен доступа?")) {
		return;
	}
	logoutButton.disabled = true;
	logoutButton.textContent = "Выходим…";
	$PI.sendToPlugin({ event: "ajazzAuthLogout" });
	setTimeout(() => {
		logoutButton.disabled = false;
		logoutButton.textContent = "Выйти из Twitch";
	}, 5000);
});
'@
    if (-not $shared.Contains($anchor)) {
        throw 'Property inspector style anchor was not found.'
    }
    $shared = $shared.Replace($anchor, $addition)
    $shared = $shared.Replace("`tif (accountKeys.length > 0) {", "`tif (accountKeys.length > 0) {`n`t`tif (logoutButton) logoutButton.style.display = `"block`";")
    $shared = $shared.Replace("`t`tnoAccountsOption.style.display = `"block`";`n`t}", "`t`tnoAccountsOption.style.display = `"block`";`n`t`tif (logoutButton) logoutButton.style.display = `"none`";`n`t}")
    [IO.File]::WriteAllText($sharedScript, $shared, $utf8NoBom)
}

Write-Host 'Applied secure Twitch logout patch.' -ForegroundColor Green
