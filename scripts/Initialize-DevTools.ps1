#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskProjectRoot = Split-Path -Parent $PSScriptRoot
$taskTools = Join-Path (Split-Path -Parent $taskProjectRoot) '.tools'
$taskVersion = (Get-Content -LiteralPath (Join-Path $taskProjectRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$taskMetadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$taskSdk = $taskMetadata.releases | ForEach-Object { $_.sdks } | Where-Object version -eq $taskVersion | Select-Object -First 1
if (-not $taskSdk) { throw '官方发布清单中找不到指定 SDK。' }
$taskFile = $taskSdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
if (-not $taskFile -or ([Uri]$taskFile.url).Host -notin @('builds.dotnet.microsoft.com','download.visualstudio.microsoft.com')) { throw 'SDK 下载来源不符合预期。' }
New-Item -ItemType Directory -Force -Path $taskTools | Out-Null
$taskArchive = Join-Path $taskTools 'dotnet-sdk.zip'
Invoke-WebRequest $taskFile.url -OutFile $taskArchive
if ((Get-FileHash -LiteralPath $taskArchive -Algorithm SHA512).Hash -ne $taskFile.hash) { throw 'SDK 校验失败。' }
Expand-Archive -LiteralPath $taskArchive -DestinationPath (Join-Path $taskTools 'dotnet') -Force
Write-Output "SDK $taskVersion 已解压到项目 .tools；系统 PATH 未修改。"

