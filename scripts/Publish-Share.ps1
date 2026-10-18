#Requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$CodexPath,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,90}$')][string]$Name='CodexQuotaBar-0.5.0-Windows-x64-Share'
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if(-not [IO.Path]::IsPathFullyQualified($CodexPath) -or -not (Test-Path -LiteralPath $CodexPath -PathType Leaf) -or [IO.Path]::GetFileName($CodexPath) -ne 'codex.exe'){throw '请指定官方codex.exe的完整路径。'}
$taskProject=[xml](Get-Content -Raw -LiteralPath (Join-Path $script:TaskProjectRoot 'src/CodexQuotaBar/CodexQuotaBar.csproj'))
$taskVersion=$taskProject.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$taskOutput=Join-Path $script:TaskProjectRoot "artifacts/$Name"
$taskSource=Join-Path $script:TaskWorkspaceRoot "work/$Name-source"
if((Test-Path -LiteralPath $taskOutput) -or (Test-Path -LiteralPath $taskSource)){throw '分享包或源码暂存目录已存在，请使用新名称。'}
foreach($taskFile in @('LICENSE.txt','NOTICE.txt')){
    if(-not(Test-Path -LiteralPath (Join-Path $script:TaskProjectRoot "third-party/codex/$taskFile") -PathType Leaf)){throw '缺少对应版本的Codex CLI许可或通知。'}
}
$taskCliVersion=(& $CodexPath --version | Out-String).Trim()
if($LASTEXITCODE -ne 0 -or $taskCliVersion -ne 'codex-cli 0.154.0'){throw '本分享包固定使用已经验证的codex-cli 0.154.0。'}
$taskApp=Join-Path $taskOutput 'app'
& $script:TaskDotnet publish (Join-Path $script:TaskProjectRoot 'src/CodexQuotaBar') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false --output $taskApp --verbosity minimal
if($LASTEXITCODE -ne 0){throw '分享版构建失败。'}
New-Item -ItemType Directory -Path (Join-Path $taskApp 'runtime') -Force | Out-Null
Copy-Item -LiteralPath $CodexPath -Destination (Join-Path $taskApp 'runtime/codex.exe')
if((Get-FileHash -LiteralPath $CodexPath).Hash -ne (Get-FileHash -LiteralPath (Join-Path $taskApp 'runtime/codex.exe')).Hash){throw '连接组件复制校验失败。'}
Set-Content -LiteralPath (Join-Path $taskApp 'share-mode.json') -Value ('{"edition":"share","version":"'+$taskVersion+'"}') -Encoding utf8
Set-Content -LiteralPath (Join-Path $taskOutput '启动.cmd') -Value "@echo off`r`nstart `"`" /D `"%~dp0app`" `"%~dp0app\CodexQuotaBar.exe`"`r`n" -Encoding ascii
foreach($taskDoc in @('使用说明.txt','使用说明.html')){
    Copy-Item -LiteralPath (Join-Path $script:TaskProjectRoot "docs/share/$taskDoc") -Destination (Join-Path $taskOutput $taskDoc)
}
$taskLicenses=Join-Path $taskOutput '组件与许可'
New-Item -ItemType Directory -Path $taskLicenses -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $script:TaskProjectRoot 'third-party/codex') -Destination (Join-Path $taskLicenses 'Codex-CLI') -Recurse
foreach($taskLegal in @('LICENSE.txt','ThirdPartyNotices.txt')){
    Copy-Item -LiteralPath (Join-Path $script:TaskSdkRoot $taskLegal) -Destination (Join-Path $taskLicenses ("dotnet-"+$taskLegal))
}
New-Item -ItemType Directory -Path $taskSource -Force | Out-Null
$taskSourceFiles=@()
foreach($taskTree in @('src','tests')){
    $taskSourceFiles+=Get-ChildItem -LiteralPath (Join-Path $script:TaskProjectRoot $taskTree) -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|TestResults)[\\/]' -and $_.Extension -in @('.cs','.csproj','.manifest') }
}
foreach($taskFile in $taskSourceFiles){
    $taskRelative=[IO.Path]::GetRelativePath($script:TaskProjectRoot,$taskFile.FullName)
    $taskDestination=Join-Path $taskSource $taskRelative
    New-Item -ItemType Directory -Path (Split-Path -Parent $taskDestination) -Force | Out-Null
    Copy-Item -LiteralPath $taskFile.FullName -Destination $taskDestination
}
foreach($taskFile in @('CodexQuotaBar.sln','Directory.Build.props','global.json','NuGet.Config')){
    Copy-Item -LiteralPath (Join-Path $script:TaskProjectRoot $taskFile) -Destination (Join-Path $taskSource $taskFile)
}
New-Item -ItemType Directory -Path (Join-Path $taskSource 'scripts'),(Join-Path $taskSource 'config'),(Join-Path $taskSource 'docs/share'),(Join-Path $taskSource 'docs/evidence') -Force | Out-Null
foreach($taskFile in @('Common.ps1','Initialize-DevTools.ps1','Build.ps1','Test.ps1','Test-Windows.ps1','Test-Live.ps1','Publish-Share.ps1')){
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $taskFile) -Destination (Join-Path $taskSource "scripts/$taskFile")
}
Copy-Item -LiteralPath (Join-Path $script:TaskProjectRoot 'config/live.example.json') -Destination (Join-Path $taskSource 'config/live.example.json')
Copy-Item -LiteralPath (Join-Path $script:TaskProjectRoot 'third-party') -Destination (Join-Path $taskSource 'third-party') -Recurse
$taskSourceReadme=Join-Path $script:TaskProjectRoot 'docs/share/源码说明.txt'
if(-not(Test-Path -LiteralPath $taskSourceReadme -PathType Leaf)){$taskSourceReadme=Join-Path $script:TaskProjectRoot '源码说明.txt'}
Copy-Item -LiteralPath $taskSourceReadme -Destination (Join-Path $taskSource '源码说明.txt')
foreach($taskDoc in @('使用说明.txt','使用说明.html')){
    Copy-Item -LiteralPath (Join-Path $script:TaskProjectRoot "docs/share/$taskDoc") -Destination (Join-Path $taskSource "docs/share/$taskDoc")
}
$taskForbidden=@('live.local.json','display.local.json','live-state.json','auth.json','config.toml')
foreach($taskRoot in @($taskOutput,$taskSource)){
    if(Get-ChildItem -LiteralPath $taskRoot -Recurse -File | Where-Object { $_.Name -in $taskForbidden -or $_.Extension -eq '.pdb' }){throw '分享内容含有个人配置或调试文件，已停止。'}
}
Compress-Archive -LiteralPath $taskSource -DestinationPath (Join-Path $taskOutput '源码.zip') -CompressionLevel Optimal
$taskFileHashes=@(Get-ChildItem -LiteralPath $taskOutput -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{file=[IO.Path]::GetRelativePath($taskOutput,$_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
})
[ordered]@{
    product='Codex额度条'; version=$taskVersion; edition='朋友分享版'; platform='Windows-x64'; selfContained=$true
    bundledCodexCli=$taskCliVersion; codexSource='https://github.com/openai/codex/releases/tag/rust-v0.154.0'
    credentialsIncluded=$false; identityBindingIncluded=$false; sourceIncluded=$true; files=$taskFileHashes
}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $taskOutput '版本信息.json') -Encoding utf8
$taskZip=$taskOutput+'.zip'
Compress-Archive -LiteralPath $taskOutput -DestinationPath $taskZip -CompressionLevel Optimal
$taskManifest=[ordered]@{version=$taskVersion;output=$taskOutput;zip=$taskZip;zipBytes=(Get-Item -LiteralPath $taskZip).Length;sha256=(Get-FileHash -LiteralPath $taskZip).Hash;createdAtUtc=[DateTimeOffset]::UtcNow}
$taskManifest|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $script:TaskProjectRoot 'artifacts/latest-share.json') -Encoding utf8
$taskManifest|ConvertTo-Json



