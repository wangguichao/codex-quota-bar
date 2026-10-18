#Requires -Version 7.0
. (Join-Path $PSScriptRoot 'Common.ps1')
$taskReport=Join-Path $script:TaskProjectRoot 'docs\evidence\live-self-test.json'
$taskProcess=Start-PrototypeProcess -Arguments @('--live-self-test',$taskReport)
if (-not $taskProcess.WaitForExit(15000)) { $taskProcess.Kill($true); throw '真实额度界面测试超时，已清理自建测试进程。' }
$taskReportData=Get-Content -Raw -LiteralPath $taskReport | ConvertFrom-Json
[pscustomobject]@{exitCode=$taskProcess.ExitCode;finishedUtc=[DateTimeOffset]::UtcNow}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $script:TaskProjectRoot 'docs\evidence\live-self-test-process.json') -Encoding utf8
if ($taskProcess.ExitCode -ne 0 -or -not $taskReportData.success) { throw '真实额度界面测试失败。' }
$taskReportData|ConvertTo-Json -Depth 12

