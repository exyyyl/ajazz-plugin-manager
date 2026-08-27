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
if ($content.Contains('the open connection stopped receiving Twitch keepalives')) {
    Write-Host 'IRC lifecycle patch is already applied.'
    return
}

$usesCrLf = $content.Contains("`r`n")
$content = $content.Replace("`r`n", "`n")

function Replace-Required {
    param(
        [Parameter(Mandatory = $true)][string]$Old,
        [Parameter(Mandatory = $true)][string]$New,
        [Parameter(Mandatory = $true)][string]$Description
    )
    if (-not $script:content.Contains($Old)) {
        throw "$Description block was not found. Apply-IrcReliability.ps1 may be missing or the upstream plugin changed."
    }
    $script:content = $script:content.Replace($Old, $New)
}

Replace-Required -Description 'IRC activity field' -Old @'
    authenticated = false;
    authenticationTimeout;
    joinedChannels = new Set();
'@ -New @'
    authenticated = false;
    authenticationTimeout;
    lastServerActivityAt = 0;
    joinedChannels = new Set();
'@

Replace-Required -Description 'IRC health state' -Old @'
    get isConnecting() {
        return this.websocket?.readyState === WebSocket$1.CONNECTING ||
            (this.websocket?.readyState === WebSocket$1.OPEN && !this.authenticated);
    }
'@ -New @'
    get isConnecting() {
        return this.websocket?.readyState === WebSocket$1.CONNECTING ||
            (this.websocket?.readyState === WebSocket$1.OPEN && !this.authenticated);
    }
    get isHealthy() {
        return this.isConnected && Date.now() - this.lastServerActivityAt < 7 * 60 * 1000;
    }
'@

Replace-Required -Description 'IRC close diagnostics' -Old @'
    onclose() {
        this.authenticated = false;
        this.websocket = undefined;
        this.joinedChannels.clear();
'@ -New @'
    onclose(event) {
        streamDeck.logger.warn(`Twitch IRC connection closed for account ${this.accountId}; code=${event?.code ?? "unknown"}, reason=${event?.reason || "none"}.`);
        this.authenticated = false;
        this.websocket = undefined;
        this.lastServerActivityAt = 0;
        this.joinedChannels.clear();
'@

Replace-Required -Description 'IRC incoming activity' -Old @'
            if ("data" in event) {
                const strings = event.data.toString().split(/\r?\n/).filter(Boolean);
'@ -New @'
            if ("data" in event) {
                this.lastServerActivityAt = Date.now();
                const strings = event.data.toString().split(/\r?\n/).filter(Boolean);
'@

Replace-Required -Description 'IRC NOTICE diagnostics' -Old @'
                        this.websocket?.close();
                        return;
                    }
                    if (!this.authenticated && (/\s001\s/i.test(line) || line.includes(" GLOBALUSERSTATE"))) {
'@ -New @'
                        this.websocket?.close();
                        return;
                    }
                    if (line.includes(" NOTICE ")) {
                        const tags = line.startsWith("@") ? this.parseIRCTags(line.substring(1, line.indexOf(" "))) : {};
                        const noticeText = line.includes(" :") ? line.substring(line.lastIndexOf(" :") + 2) : "No details";
                        streamDeck.logger.warn(`Twitch IRC notice${tags["msg-id"] ? ` [${tags["msg-id"]}]` : ""}: ${noticeText}`);
                    }
                    if (!this.authenticated && (/\s001\s/i.test(line) || line.includes(" GLOBALUSERSTATE"))) {
'@

Replace-Required -Description 'IRC close event forwarding' -Old @'
            this.websocket.onclose = () => this.onclose();
'@ -New @'
            this.websocket.onclose = (event) => this.onclose(event);
'@

Replace-Required -Description 'IRC stale-connection recovery' -Old @'
        await this.connectionEstablished();
    }
    // Function called to join a channel
'@ -New @'
        await this.connectionEstablished();
    }
    async reconnect(reason) {
        streamDeck.logger.debug(`Reconnecting Twitch IRC for account ${this.accountId}: ${reason}.`);
        const previousWebsocket = this.websocket;
        this.websocket = undefined;
        this.authenticated = false;
        this.lastServerActivityAt = 0;
        this.joinedChannels.clear();
        this.availableRetries = 5;
        this._rejectConnection?.(new Error(`IRC reconnect requested: ${reason}`));
        this.clearConnectionPromiseHandlers();
        if (previousWebsocket) {
            previousWebsocket.onopen = null;
            previousWebsocket.onmessage = null;
            previousWebsocket.onclose = null;
            previousWebsocket.onerror = null;
            try {
                previousWebsocket.close();
            }
            catch { }
        }
        await this.connect();
    }
    async ensureConnected(token) {
        if (this.token !== token) {
            this.token = token;
            await this.reconnect("the Twitch access token was refreshed");
            return;
        }
        if (this.isHealthy)
            return;
        if (this.isConnected) {
            await this.reconnect("the open connection stopped receiving Twitch keepalives");
            return;
        }
        await this.connect();
    }
    // Function called to join a channel
'@

Replace-Required -Description 'IRC send diagnostics' -Old @'
        message.split("\n").forEach((line) => {
            this.websocket.send(`PRIVMSG #${channel} :${line}`);
        });
'@ -New @'
        message.split("\n").forEach((line) => {
            this.websocket.send(`PRIVMSG #${channel} :${line}`);
        });
        streamDeck.logger.debug(`Queued Twitch IRC chat message for #${channel}.`);
'@

Replace-Required -Description 'IRC connection health check' -Old @'
        if (!ircSocket.isConnected)
            await ircSocket.connect();
'@ -New @'
        await ircSocket.ensureConnected(token);
'@

Replace-Required -Description 'Chat credential refresh' -Old @'
    async sendChatMessage(accountId, message, channelName) {
        const ircSocket = await pluginStore.getConnectedIRCWebsocket(accountId);
'@ -New @'
    async sendChatMessage(accountId, message, channelName) {
        await syncAjazzAccountToken(accountId);
        const ircSocket = await pluginStore.getConnectedIRCWebsocket(accountId);
'@

Replace-Required -Description 'In-memory token synchronization' -Old @'
async function ensureAjazzCredential() {
    if (!ajazzCredentialPromise) {
        ajazzCredentialPromise = ensureAjazzCredentialCore().finally(() => {
            ajazzCredentialPromise = undefined;
        });
    }
    return ajazzCredentialPromise;
}
'@ -New @'
async function ensureAjazzCredential() {
    if (!ajazzCredentialPromise) {
        ajazzCredentialPromise = ensureAjazzCredentialCore().finally(() => {
            ajazzCredentialPromise = undefined;
        });
    }
    return ajazzCredentialPromise;
}

async function syncAjazzAccountToken(accountId) {
    const credential = await ensureAjazzCredential();
    if (!credential || credential.userId !== accountId)
        return;
    const account = pluginStore.accounts.get(accountId);
    if (account && account.token !== credential.accessToken) {
        account.token = credential.accessToken;
        streamDeck.logger.debug(`Updated the in-memory Twitch token for account ${accountId}.`);
    }
}
'@

if ($usesCrLf) {
    $content = $content.Replace("`n", "`r`n")
}
[IO.File]::WriteAllText($pluginScript, $content, (New-Object System.Text.UTF8Encoding($false)))
Write-Host 'Applied Twitch IRC lifecycle patch.' -ForegroundColor Green
