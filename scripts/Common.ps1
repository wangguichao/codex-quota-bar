Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:TaskProjectRoot = Split-Path -Parent $PSScriptRoot
$script:TaskWorkspaceRoot = Split-Path -Parent $script:TaskProjectRoot
$script:TaskSdkRoot = Join-Path $script:TaskWorkspaceRoot '.tools\dotnet'
$script:TaskDotnet = Join-Path $script:TaskSdkRoot 'dotnet.exe'
if (-not (Test-Path -LiteralPath $script:TaskDotnet)) { throw '请先运行 scripts/Initialize-DevTools.ps1。' }
$env:DOTNET_ROOT = $script:TaskSdkRoot
$env:DOTNET_CLI_HOME = Join-Path $script:TaskWorkspaceRoot '.tools\cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
function Start-PrototypeProcess {
    param([string[]]$Arguments,[ValidateSet('Debug','Release')][string]$Configuration='Release')
    $taskBinary = Join-Path $script:TaskProjectRoot "src\CodexQuotaBar\bin\$Configuration\net10.0-windows\CodexQuotaBar.exe"
    if (-not (Test-Path -LiteralPath $taskBinary)) { throw '请先运行 scripts/Build.ps1。' }
    $taskInfo = [Diagnostics.ProcessStartInfo]::new($taskBinary)
    $taskInfo.UseShellExecute = $false
    $taskInfo.CreateNoWindow = $true
    foreach ($taskArgument in $Arguments) { $taskInfo.ArgumentList.Add($taskArgument) }
    return [Diagnostics.Process]::Start($taskInfo)
}



