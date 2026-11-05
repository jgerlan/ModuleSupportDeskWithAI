$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$envFile = Join-Path $projectRoot '.env'
$key = $null
if (Test-Path -LiteralPath $envFile) {
    $line = Get-Content -LiteralPath $envFile | Where-Object { $_ -match '^LITELLM_MASTER_KEY=.' } | Select-Object -Last 1
    if ($line) { $key = $line.Substring('LITELLM_MASTER_KEY='.Length).Trim().Trim('"').Trim("'") }
}
if (!$key) {
    $bytes = New-Object byte[] 32
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($bytes) } finally { $random.Dispose() }
    $key = 'sk-' + [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()
    Add-Content -LiteralPath $envFile -Value ([Environment]::NewLine + 'LITELLM_MASTER_KEY=' + $key)
}
$project = Join-Path $projectRoot 'src/AI.SupportDesk.Api'
$localFile = Join-Path $project 'appsettings.Local.json'
$localSettings = @{}
if (Test-Path -LiteralPath $localFile) {
    $parsed = Get-Content -Raw -LiteralPath $localFile | ConvertFrom-Json
    foreach ($property in $parsed.PSObject.Properties) {
        $localSettings[$property.Name] = $property.Value
    }
}
$liteLlmSettings = @{}
if ($localSettings.ContainsKey('LiteLLM') -and $null -ne $localSettings['LiteLLM']) {
    foreach ($property in $localSettings['LiteLLM'].PSObject.Properties) {
        $liteLlmSettings[$property.Name] = $property.Value
    }
}
$liteLlmSettings['ApiKey'] = $key
$localSettings['LiteLLM'] = $liteLlmSettings
$localSettings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $localFile -Encoding UTF8
& dotnet user-secrets set 'LiteLLM:ApiKey' $key --project $project | Out-Null
if ($LASTEXITCODE) { throw 'Could not save the LiteLLM API key in User Secrets.' }
Write-Output 'LiteLLM key configured in ignored .env, appsettings.Local.json and development User Secrets. Restart the backend.'

