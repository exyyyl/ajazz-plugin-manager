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
if ($content.Contains('Twitch IRC authentication timed out.')) {
    Write-Host 'IRC reliability patch is already applied.'
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
        throw "$Description block was not found. The upstream Twitch plugin version may have changed."
    }
    $script:content = $script:content.Replace($Old, $New)
}

Replace-Required -Description 'IRC fields and connection state' -Old @'
    availableRetries = 5;
    websocket;
    joinedChannels = new Set();
'@ -New @'
    availableRetries = 5;
    websocket;
    authenticated = false;
    authenticationTimeout;
    joinedChannels = new Set();
'@

Replace-Required -Description 'IRC state getters' -Old @'
    get isConnected() {
        return this.websocket?.readyState === WebSocket$1.OPEN;
    }
    get isConnecting() {
        return this.websocket?.readyState === WebSocket$1.CONNECTING;
    }
'@ -New @'
    get isConnected() {
        return this.websocket?.readyState === WebSocket$1.OPEN && this.authenticated;
    }
    get isConnecting() {
        return this.websocket?.readyState === WebSocket$1.CONNECTING ||
            (this.websocket?.readyState === WebSocket$1.OPEN && !this.authenticated);
    }
'@

Replace-Required -Description 'IRC authentication handshake' -Old @'
    clearConnectionPromiseHandlers() {
        this._connectionPromise = undefined;
        this._resolveConnection = undefined;
        this._rejectConnection = undefined;
    }
    async onopen() {
        const channel = await twitchService.getChannelName(this.accountId, this.accountId);
        // Set password and nickname
        this.websocket.send("PASS oauth:" + this.token);
        this.websocket.send("NICK " + channel);
        // Request the commands and tags in order to get notified when the ROOMSTATE changes:
        // when a user joins a channel or a room setting is changed.
        this.websocket.send("CAP REQ :twitch.tv/commands twitch.tv/tags");
        this._resolveConnection?.();
        this.clearConnectionPromiseHandlers();
    }
    onclose() {
        this.websocket = undefined;
        this.joinedChannels.clear();
        this._rejectConnection?.(new Error("WebSocket closed before open"));
        this.clearConnectionPromiseHandlers();
        if (this.availableRetries > 0) {
            this.availableRetries--;
            this.connect();
        }
    }
'@ -New @'
    clearConnectionPromiseHandlers() {
        if (this.authenticationTimeout) {
            clearTimeout(this.authenticationTimeout);
            this.authenticationTimeout = undefined;
        }
        this._connectionPromise = undefined;
        this._resolveConnection = undefined;
        this._rejectConnection = undefined;
    }
    async onopen() {
        const channel = pluginStore.accounts.get(this.accountId)?.loginName ??
            await twitchService.getChannelName(this.accountId, this.accountId);
        if (!channel)
            throw new Error(`Unable to find the Twitch login name for account ${this.accountId}.`);
        // Set password and nickname
        this.websocket.send("PASS oauth:" + this.token);
        this.websocket.send("NICK " + channel);
        // Request the commands and tags in order to get notified when the ROOMSTATE changes:
        // when a user joins a channel or a room setting is changed.
        this.websocket.send("CAP REQ :twitch.tv/commands twitch.tv/tags");
        // An open WebSocket is not an authenticated IRC session yet. Twitch can
        // silently discard JOIN/PRIVMSG commands sent before its 001 welcome.
        this.authenticationTimeout = setTimeout(() => {
            const error = new Error("Twitch IRC authentication timed out.");
            this._rejectConnection?.(error);
            this.clearConnectionPromiseHandlers();
            this.websocket?.close();
        }, 10000);
    }
    onclose() {
        this.authenticated = false;
        this.websocket = undefined;
        this.joinedChannels.clear();
        this._rejectConnection?.(new Error("WebSocket closed before open"));
        this.clearConnectionPromiseHandlers();
        if (this.availableRetries > 0) {
            this.availableRetries--;
            this.connect().catch((error) => streamDeck.logger.error("Unable to reconnect Twitch IRC WebSocket.", error));
        }
    }
'@

Replace-Required -Description 'IRC protocol handling' -Old @'
    handleMessage(event) {
        try {
            if ("data" in event) {
                const strings = event.data.toString().split("\n");
                const lastIndex = strings.length - 1;
                if (lastIndex > 0) {
                    strings.pop();
                }
                // Handle one line at a time
                strings.forEach(this.handleTagMessage.bind(this));
            }
        }
        catch (e) {
            streamDeck.logger.error("Error handling IRC message", e);
        }
    }
'@ -New @'
    handleMessage(event) {
        try {
            if ("data" in event) {
                const strings = event.data.toString().split(/\r?\n/).filter(Boolean);
                // Handle one line at a time
                strings.forEach((line) => {
                    if (line.startsWith("PING ")) {
                        this.websocket?.send(line.replace(/^PING/, "PONG"));
                        return;
                    }
                    if (line.includes(" NOTICE * :Login authentication failed") ||
                        line.includes(" NOTICE * :Improperly formatted auth")) {
                        const error = new Error(line.substring(line.indexOf("NOTICE * :") + 10));
                        this.availableRetries = 0;
                        this._rejectConnection?.(error);
                        this.clearConnectionPromiseHandlers();
                        this.websocket?.close();
                        return;
                    }
                    if (!this.authenticated && (/\s001\s/i.test(line) || line.includes(" GLOBALUSERSTATE"))) {
                        this.authenticated = true;
                        this.availableRetries = 5;
                        this._resolveConnection?.();
                        this.clearConnectionPromiseHandlers();
                        streamDeck.logger.debug(`Authenticated Twitch IRC connection for account ${this.accountId}.`);
                    }
                    this.handleTagMessage(line);
                });
            }
        }
        catch (e) {
            streamDeck.logger.error("Error handling IRC message", e);
        }
    }
'@

Replace-Required -Description 'IRC open handler' -Old @'
            this.websocket.onopen = () => this.onopen();
'@ -New @'
            this.websocket.onopen = () => this.onopen().catch((error) => {
                this._rejectConnection?.(error);
                this.clearConnectionPromiseHandlers();
                this.websocket?.close();
            });
'@

Replace-Required -Description 'IRC joined-state check' -Old @'
        if (!this.websocket)
            throw "Cannot join IRC channel, no websocket connection.";
'@ -New @'
        if (!this.isConnected)
            throw "Cannot join IRC channel, websocket is not authenticated.";
'@

Replace-Required -Description 'IRC send-state check' -Old @'
        if (!this.websocket.OPEN)
            throw `Failed to send message to channel ${channel}, websocket not open.`;
'@ -New @'
        if (!this.authenticated || this.websocket.readyState !== WebSocket$1.OPEN)
            throw `Failed to send message to channel ${channel}, websocket not open.`;
'@

Replace-Required -Description 'Twitch login-name lookup' -Old @'
    async getChannelName(accountId, broadcasterId) {
        let channelName;
'@ -New @'
    async getChannelName(accountId, broadcasterId) {
        await pluginStore.accountsReady;
        let channelName;
'@

Replace-Required -Description 'Own-channel login name' -Old @'
            channelName = pluginStore.accounts.get(accountId)?.name;
'@ -New @'
            channelName = pluginStore.accounts.get(accountId)?.loginName;
'@

if ($usesCrLf) {
    $content = $content.Replace("`n", "`r`n")
}
[IO.File]::WriteAllText($pluginScript, $content, (New-Object System.Text.UTF8Encoding($false)))
Write-Host 'Applied Twitch IRC reliability patch.' -ForegroundColor Green
