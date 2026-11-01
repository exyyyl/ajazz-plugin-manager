param(
    [Parameter(Mandatory = $true)][string]$PluginPath,
    [Parameter(Mandatory = $true)][string]$ProfileRoot,
    [Parameter(Mandatory = $true)][string]$BackupRoot
)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $ProfileRoot)) { return }
$english = Get-Content -LiteralPath "$PluginPath\en.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$russian = Get-Content -LiteralPath "$PluginPath\ru.json" -Raw -Encoding UTF8 | ConvertFrom-Json
function Convert-ActionNames($value) {
    if ($value -is [Array]) { foreach ($item in $value) { Convert-ActionNames $item }; return }
    if ($value -isnot [PSCustomObject]) { return }
    $id = $value.UUID
    if ($id -and $id.StartsWith('com.elgato.discord.') -and $value.Name -eq $english.$id.Name -and $russian.$id.Name) {
        $value.Name = $russian.$id.Name
        $script:changed = $true
    }
    foreach ($property in $value.PSObject.Properties) { Convert-ActionNames $property.Value }
}
$root = [IO.Path]::GetFullPath($ProfileRoot).TrimEnd('\') + '\'
foreach ($file in Get-ChildItem -LiteralPath $ProfileRoot -Filter manifest.json -Recurse -File) {
    $profile = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    $script:changed = $false
    Convert-ActionNames $profile
    if (!$script:changed) { continue }
    $relative = $file.FullName.Substring($root.Length)
    $backupFile = Join-Path $BackupRoot $relative
    New-Item -ItemType Directory -Path (Split-Path $backupFile) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $backupFile
    $profile | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $file.FullName -Encoding UTF8
    Write-Output "Localized default Discord action names: $relative"
}
