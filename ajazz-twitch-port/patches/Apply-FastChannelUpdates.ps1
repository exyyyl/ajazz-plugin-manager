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
if ($content.Contains('Updated Twitch stream title/category in')) {
    Write-Host 'Fast channel updates patch is already applied.'
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

Replace-Required -Description 'Combined stream update token sync' -Old @'
    async setTitleAndGame(accountId, title, gameId) {
        const token = await pluginStore.getAccountToken(accountId);
'@ -New @'
    async setTitleAndGame(accountId, title, gameId) {
        await syncAjazzAccountToken(accountId);
        const token = await pluginStore.getAccountToken(accountId);
'@

Replace-Required -Description 'Combined stream update timing' -Old @'
        const response = await fetch(url, options);
        const { status } = response;
        if (!this.successStatus.includes(status)) {
            const { message, status, error } = (await response.json());
            throw `(${status}) ${error}: ${message}`;
        }
    }
    /**
     * Retrieves the viewer count for a live Twitch channel.
'@ -New @'
        const startedAt = Date.now();
        const response = await fetch(url, options);
        const { status } = response;
        if (!this.successStatus.includes(status)) {
            const { message, status, error } = (await response.json());
            throw `(${status}) ${error}: ${message}`;
        }
        streamDeck.logger.debug(`Updated Twitch stream title/category in ${Date.now() - startedAt} ms.`);
    }
    /**
     * Retrieves the viewer count for a live Twitch channel.
'@

Replace-Required -Description 'Stream title update timing' -Old @'
    async setStreamTitle(accountId, title) {
        const token = await pluginStore.getAccountToken(accountId);
'@ -New @'
    async setStreamTitle(accountId, title) {
        await syncAjazzAccountToken(accountId);
        const token = await pluginStore.getAccountToken(accountId);
'@

Replace-Required -Description 'Stream title response timing' -Old @'
        const response = await fetch(url, options);
        const { status } = response;
        if (!this.successStatus.includes(status)) {
            const { message, status, error } = (await response.json());
            throw `(${status}) ${error}: ${message}`;
        }
    }
    /**
     * Updates the category of a livestream.
'@ -New @'
        const startedAt = Date.now();
        const response = await fetch(url, options);
        const { status } = response;
        if (!this.successStatus.includes(status)) {
            const { message, status, error } = (await response.json());
            throw `(${status}) ${error}: ${message}`;
        }
        streamDeck.logger.debug(`Updated Twitch stream title in ${Date.now() - startedAt} ms.`);
    }
    /**
     * Updates the category of a livestream.
'@

Replace-Required -Description 'Stream category update timing' -Old @'
    async setStreamCategory(accountId, categoryId) {
        const token = await pluginStore.getAccountToken(accountId);
'@ -New @'
    async setStreamCategory(accountId, categoryId) {
        await syncAjazzAccountToken(accountId);
        const token = await pluginStore.getAccountToken(accountId);
'@

Replace-Required -Description 'Stream category response timing' -Old @'
        const response = await fetch(url, options);
        const { status } = response;
        if (!this.successStatus.includes(status)) {
            const { message, status, error } = (await response.json());
            throw `(${status}) ${error}: ${message}`;
        }
    }
    /**
     * Get Moderated Channels
'@ -New @'
        const startedAt = Date.now();
        const response = await fetch(url, options);
        const { status } = response;
        if (!this.successStatus.includes(status)) {
            const { message, status, error } = (await response.json());
            throw `(${status}) ${error}: ${message}`;
        }
        streamDeck.logger.debug(`Updated Twitch stream category in ${Date.now() - startedAt} ms.`);
    }
    /**
     * Get Moderated Channels
'@

Replace-Required -Description 'Category key-down action' -Old @'
        async onKeyUp({ action, payload }) {
            try {
                const { settings } = payload;
                const { accountId = pluginStore.getDefaultAccountId(), categoryId } = settings;
'@ -New @'
        async onKeyDown({ action, payload }) {
            try {
                const { settings } = payload;
                const { accountId = pluginStore.getDefaultAccountId(), categoryId } = settings;
'@

Replace-Required -Description 'Combined stream key-down action' -Old @'
        async onKeyUp({ action, payload }) {
            try {
                const { settings } = payload;
                const { ChannelStatus, accountId, ChannelGameID } = settings;
'@ -New @'
        async onKeyDown({ action, payload }) {
            try {
                const { settings } = payload;
                const { ChannelStatus, accountId, ChannelGameID } = settings;
'@

Replace-Required -Description 'Title key-down action' -Old @'
        async onKeyUp({ action, payload }) {
            try {
                const { settings } = payload;
                const { accountId = pluginStore.getDefaultAccountId(), title } = settings;
'@ -New @'
        async onKeyDown({ action, payload }) {
            try {
                const { settings } = payload;
                const { accountId = pluginStore.getDefaultAccountId(), title } = settings;
'@

if ($usesCrLf) {
    $content = $content.Replace("`n", "`r`n")
}
[IO.File]::WriteAllText($pluginScript, $content, (New-Object System.Text.UTF8Encoding($false)))
Write-Host 'Applied fast Twitch channel updates patch.' -ForegroundColor Green
