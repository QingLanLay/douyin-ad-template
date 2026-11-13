param([string]$ReportDirectory, [string]$ProjectPath)
$ErrorActionPreference='Stop'
$project=if($ProjectPath){[IO.Path]::GetFullPath($ProjectPath)}else{[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))}
if(-not $ReportDirectory){$ReportDirectory=Join-Path $project '.artifacts\FeedAudioTests'}
$ReportDirectory=[IO.Path]::GetFullPath($ReportDirectory)
[void][IO.Directory]::CreateDirectory($ReportDirectory)
$sdk=Get-ChildItem -LiteralPath 'C:\Program Files\dotnet\sdk' -Directory | Sort-Object Name -Descending | Select-Object -First 1
$refs=Get-ChildItem -LiteralPath 'C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref' -Directory | Sort-Object Name -Descending | Select-Object -First 1
$refDir=Get-ChildItem -LiteralPath (Join-Path $refs.FullName 'ref') -Directory | Select-Object -First 1
$core=Join-Path $project 'Assets\DouYinIconDemo\推荐流通用接入包\Assets\抖音\推荐流\Core'
$dll=Join-Path $ReportDirectory 'FeedPlaybackControllerTests.dll'
$response=@('/nologo','/target:exe','/langversion:9.0','/nostdlib+',('/out:"'+$dll+'"'))
$response+=Get-ChildItem -LiteralPath $refDir.FullName -Filter '*.dll' -File | ForEach-Object {'/reference:"'+$_.FullName+'"'}
$response+=@(('"'+(Join-Path $core 'FeedPlaybackController.cs')+'"'),('"'+(Join-Path $core 'FeedVoiceTimeline.cs')+'"'),('"'+(Join-Path $PSScriptRoot 'FeedPlaybackControllerTests.cs')+'"'))
$rsp=Join-Path $ReportDirectory 'controller-tests.rsp'
[IO.File]::WriteAllLines($rsp,$response,[Text.UTF8Encoding]::new($false))
& dotnet (Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll') ('@'+$rsp)
if($LASTEXITCODE -ne 0){throw '控制器测试编译失败。'}
$runtime=@{runtimeOptions=@{tfm='net9.0';framework=@{name='Microsoft.NETCore.App';version='9.0.0'}}}|ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $ReportDirectory 'FeedPlaybackControllerTests.runtimeconfig.json'),$runtime,[Text.UTF8Encoding]::new($false))
& dotnet $dll (Join-Path $ReportDirectory 'controller-tests.json')
if($LASTEXITCODE -ne 0){throw '控制器测试存在失败项。'}