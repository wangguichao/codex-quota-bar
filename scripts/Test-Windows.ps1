#Requires -Version 7.0
. (Join-Path $PSScriptRoot 'Common.ps1')
$taskReport = Join-Path $script:TaskProjectRoot 'docs\evidence\window-self-test.json'
$taskProcess = Start-PrototypeProcess -Arguments @('--self-test',$taskReport)
if (-not $taskProcess.WaitForExit(30000)) { $taskProcess.Kill($true); throw '桌面测试超时，已结束自建测试进程。' }
if ($taskProcess.ExitCode -ne 0) { throw '桌面测试失败，详见报告。' }
Get-Content -LiteralPath $taskReport

