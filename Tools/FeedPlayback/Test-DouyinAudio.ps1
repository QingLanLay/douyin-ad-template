param([string]$ReportDirectory, [string]$ProjectPath)
$ErrorActionPreference = 'Stop'
$root = if ($ProjectPath) { [IO.Path]::GetFullPath($ProjectPath) } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')) }
if (-not $ReportDirectory) { $ReportDirectory = Join-Path $root '.artifacts\FeedAudioTests' }
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
$output = Join-Path $ReportDirectory 'sdk-tests'
[void][IO.Directory]::CreateDirectory($output)
$core = Join-Path $root 'Assets\DouYinIconDemo\推荐流通用接入包\Assets\抖音\推荐流\Core'
$paths = @(
    (Join-Path $core 'FeedDouyinAudio.cs'),
    (Join-Path $core 'FeedPlaybackController.UnityAudio.cs'),
    (Join-Path $core 'FeedPlaybackController.cs'),
    (Join-Path $core 'FeedVoiceTimeline.cs'),
    (Join-Path $PSScriptRoot 'DouyinAudio\Program.cs'),
    (Join-Path $PSScriptRoot 'DouyinAudio\Sdk.cs')
)
$items = $paths | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_) + '" />' }
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit><DefineConstants>UNITY_WEBGL</DefineConstants></PropertyGroup><ItemGroup>' + ($items -join '') + '</ItemGroup></Project>'
$projectPath = Join-Path $output 'DouyinAudioTests.csproj'
[IO.File]::WriteAllText($projectPath, $project, [Text.UTF8Encoding]::new($false))
& dotnet run --project $projectPath --configuration Release -- (Join-Path $ReportDirectory 'sdk-audio-tests.json')
if ($LASTEXITCODE -ne 0) { throw "抖音播放器测试失败：$LASTEXITCODE" }
