# 抖音推荐流通用接入包

## 组件

- `FeedRecommendationManager`：启动参数、配置、获客与复访路由、平台事件与 7001 接口，持有唯一 `Playback`。
- `FeedPlaybackController`：统一管理资源准备、首画面、上报、配音字幕、暂停、重试与会话释放。
- `FeedPlaybackController.UnityAudio`：按环境使用抖音播放器或 Editor/浏览器 AudioSource。
- `FeedDouyinAudio`：通过 TTAudioManager 播放配音，处理异步创建、当前音源状态读取和延迟释放。
- `FeedVoiceTimeline`：根据本次音源确认后的播放位置选择字幕。
- `FeedPlaybackView`：音频与 Cue 绑定、字幕预制体、错误页面和背景音乐音量。
- `FeedRecommendationProjectAdapter`：模板首页适配。实际游戏修改资源、首页和完整游戏入口，保留控制器。
- `FeedVideoPlayerController`：视频方案的准备、首画面与卡片可见播放。

## 预制体

`Resources/FeedPlayback.prefab` 包含播放器、AudioSource、错误页面、重试按钮与嵌套 `FeedSubtitle.prefab`。

字幕源在 `Assets/抖音/推荐流/UI/FeedSubtitle.prefab`。字体为独立 `FeedSubtitleFont.asset`，来源为 DouyinSansBold；专用 `FeedSubtitle.mat` 使用黑色描边和 Underlay 阴影。保留嵌套源关系，不运行时创建替代 TMP。

模板在推荐流准备时调用 `Resources.Load<FeedPlaybackView>("FeedPlayback")`。实际游戏可以将同一字幕源预制体放入现有首页 Canvas，序列化引用并按屏幕调整位置。

## 首页配音资源

在 FeedPlaybackView 的 Narrations 中填写：

- `key`：当前内容资源键，模板默认首页为 `home`。
- `clip` 或 `audioUrl`：最终真实配音，二选一；相对 URL 从 StreamingAssets 读取。
- `cues`：包含同一 key 的字幕 JSON。

```json
{"key":"home","duration":11.2,"samples":358400,"sampleRate":32000,"cues":[{"start":0,"end":5.1,"text":"第一段介绍"},{"start":5.1,"end":11.2,"text":"第二段介绍"}]}
```

分段连续、非空、递增，最后结束时间对应真实音频。实际游戏要求配音时关闭 `allowSilentPreview`。模板没有游戏文案和配音，默认允许显式无配音首页预览；不会生成替代音频。

抖音容器使用抖音播放器。`TTAudioManager.CanUseNativeAudio()` 为真时，通过 `FeedDouyinAudio` 调用 `TTAudioManager.CreateInnerAudioContext`；能力不可用时使用公开 `tt.createInnerAudioContext`。直接绑定本次下载的编码数据并读取当前实例的播放确认、位置、缓冲与结束状态。SDK 支持 WAV、MP3、M4A 和 AAC；桌面开发者工具使用 `ttfile://user`，手机使用 `TTFileSystemManager.USER_DATA_PATH`，写文件地址与音源 src 保持一致。`Assets/Plugins/WebGL/FeedPlaybackAudio.jslib` 随广告包导入。无需改写 framework 的音频导入表。本地 AudioClip 在抖音容器需要可读取 PCM 的导入设置；远程编码数据通过 `BindEncoded` 绑定。

## 生命周期

1. SDK 沿项目唯一入口初始化。适配层注册完成后，管理器解析 Scene 后缀 `3041` 与渠道参数。
2. `SchemePrepared` 调用 `Playback.Begin(version,key,now)`，隐藏准备真实首页。
3. `IHost.Prepare` 提交本次资源。只 yield null 或嵌套 IEnumerator，异步任务检查 Preparation.IsCurrent。
4. `PresentContent` 和 `FlushPresentation` 确认实际画面与输入可用，控制器提交 7001。
5. 保存本会话仍有效的 OnShow 请求，准备完成后自动传递给播放器。OnHide 取消待播请求或暂停同一音源。SDK 音源标识尚未返回时继续等待；实际起播并取得本次显示期间的新位置后显示字幕。
6. OnHide 调用 Hide，同位置暂停。恢复保持同一音源，不补算后台时间。
7. FeedEnter 调用 EnterGame，停止卡片播报并交给原完整游戏入口。提前进入时保存意图，等首页可用后执行一次。
8. FeedExit 调用 ExitGame，完整游戏前后台调用 SetFullGameVisible。
9. 会话更换调用 EndSession，销毁调用 Dispose，释放旧请求、音源、字幕与 BGM 状态。

隐藏阶段只准备，不推进正式计时、AI、存档、奖励或声音。项目宿主管理自己的暂停来源，释放时保留其他暂停。

## 准备与上报

控制器默认准备期限 90 秒，7001 回包期限 8 秒，最多 3 次尝试。资源失败或超时显示独立错误页面及重试按钮；错误页面可展示后可提交兜底就绪。成功后停止上报，旧会话和旧尝试回调失效。

首画面准备不能依赖暂停中的正式开局。IHost.Available 应指向真实首页，FlushPresentation 检查 UI、输入与目标输出画面。加载进度、JSON 和 sceneLoaded 单独不足以确认首页可展示。

## 配置与路由

本地配置为 `Resources/FeedRecommendationConfig.asset`。优先同渠道 extra.route 白名单，然后 ContentID，最后同渠道首页。获客 channel=2、scene=0，复访 channel=1、scene=1/2/3。普通启动沿原流程。

真实游戏的 RemoteConfigUrl 按项目英文名配置，服务器 JSON 格式为：

```json
{"items":[{"id":"真实ContentID","c":2,"r":0,"s":2,"p":"home"}]}
```

远程不可用时读取本地配置。`SIM_` ID 用于本地获客与复访模拟，不提交平台 StoreFeedData。真实 ID、AppID、广告位和服务器上传按目标项目配置。

## 复访

正式业务入口触发订阅，按 Scene + ContentID 上报复访就绪并记录实际成功或失败。播放器负责展示和交接，不替代业务条件、平台配置或真实回包。

## 编辑器与验证

使用推荐流调试窗口分别模拟获客、复访、7001、OnShow、OnHide、FeedEnter 与 FeedExit。可在准备之前发出一次 OnShow，确认准备完成后开始卡片播放；准备期间再发 OnHide 时保持隐藏。

`FeedPlaybackAssets.Prepare()` 检查播放器、嵌套字幕和字体材质。工程内 Tools/FeedPlayback 提供独立控制器、抖音 SDK 异步音频及原生/浏览器接口模拟测试。管理器持续执行 `FeedDouyinAudio.CleanupRetired()`，释放取消后才取得音源标识的实例与临时文件。平台真实卡片、7001 回包、订阅与手机表现分别验证。

## 导入

2022 与 6000 广告包分别由对应编辑器导出。导入后使用相应 Unity 版本的 TMP 资源。项目仅保留一个 FeedRecommendationManager、一个播放器和一套平台监听。现有 SDK 初始化、存档和正式游戏入口继续复用。
