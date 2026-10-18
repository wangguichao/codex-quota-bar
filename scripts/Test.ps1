#Requires -Version 7.0
. (Join-Path $PSScriptRoot 'Common.ps1')
& $script:TaskDotnet test (Join-Path $script:TaskProjectRoot 'tests\CodexQuotaBar.Tests') --collect:'XPlat Code Coverage' --logger 'console;verbosity=minimal'
if ($LASTEXITCODE -ne 0) { throw '自动测试失败。' }

