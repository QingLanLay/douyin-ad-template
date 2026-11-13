param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [string]$ReportDirectory
)
$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).ProviderPath
if (-not (Test-Path -LiteralPath (Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt'))) {
    throw "Unity 项目版本文件不存在：$ProjectPath"
}
if (-not $ReportDirectory) { $ReportDirectory = Join-Path $ProjectPath '.artifacts\FeedAudioTests' }
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
[void][IO.Directory]::CreateDirectory($ReportDirectory)
& (Join-Path $PSScriptRoot 'Test-DouyinAudio.ps1') -ProjectPath $ProjectPath -ReportDirectory $ReportDirectory
& (Join-Path $PSScriptRoot 'Test-FeedPlaybackController.ps1') -ProjectPath $ProjectPath -ReportDirectory $ReportDirectory
foreach ($script in @('Test-NativeVoice.mjs', 'Test-BrowserVoice.mjs')) {
    & node (Join-Path $PSScriptRoot $script) $ReportDirectory $ProjectPath
    if ($LASTEXITCODE -ne 0) { throw "音频接口测试失败：$script" }
}
