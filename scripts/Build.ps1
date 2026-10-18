#Requires -Version 7.0
param([ValidateSet('Debug','Release')][string]$Configuration='Release')
. (Join-Path $PSScriptRoot 'Common.ps1')
& $script:TaskDotnet build (Join-Path $script:TaskProjectRoot 'CodexQuotaBar.sln') --configuration $Configuration --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw '构建失败。' }

