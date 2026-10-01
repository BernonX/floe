# Prefer the workspace SDK without changing the machine's PATH or installed runtimes.
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Arguments
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $workspaceRoot '.tools/dotnet/dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localSdk) {
    $localSdk
} else {
    (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
}

$environmentOverrides = @{
    DOTNET_CLI_HOME = Join-Path $workspaceRoot '.tools/cli-home'
    NUGET_PACKAGES = Join-Path $workspaceRoot '.tools/nuget-packages'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    DOTNET_NOLOGO = '1'
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
}
if ($dotnetCommand -eq $localSdk) {
    $environmentOverrides['DOTNET_ROOT'] = Split-Path -Parent $localSdk
    $environmentOverrides['DOTNET_ROOT_X64'] = Split-Path -Parent $localSdk
}

$previousEnvironment = @{}
foreach ($name in $environmentOverrides.Keys) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $environmentOverrides[$name], 'Process')
}

Push-Location $workspaceRoot
try {
    & $dotnetCommand @Arguments
    $commandExitCode = $LASTEXITCODE
} finally {
    Pop-Location
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
}
exit $commandExitCode
