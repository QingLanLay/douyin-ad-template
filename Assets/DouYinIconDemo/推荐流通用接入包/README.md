# 抖音小游戏推荐流直玩通用接入包

该目录提供可随完整 `.unitypackage` 导入其他 Unity 游戏的推荐流直玩组件，负责启动参数解析、内容路由、场景就绪、全场景订阅、Scene 1/2/3 默认就绪及平台生命周期。

导入完整模板包后，把 `Assets/DouYinIconDemo/Prefabs/Systems/DouyinAdSystem.prefab` 拖入首场景即可启用默认适配层。平台 ContentID、远程配置地址和目标游戏关卡加载仍属于项目数据，必须按实际游戏填写；模板不会虚构这些值或覆盖目标游戏存档。

## 适用范围

- Unity 小游戏项目。
- 已接入 TTSDK，且已有唯一 `TT.InitSDK` 初始化入口。
- 需要按推荐流 ContentID 分配视频、试玩关卡或新手引导。
- 需要远程动态配置，并保留本地安全兜底。

仓库没有完整 Unity 工程和 TTSDK 版本文件，无法在此处验证固定版本组合。请在目标项目中核对实际 TTSDK 版本，并以抖音开放平台当前要求为准。

## 目录结构

```text
推荐流通用接入包/
├─ Assets/抖音/推荐流/
│  ├─ Core/
│  │  ├─ FeedRecommendationConfig.cs
│  │  ├─ FeedRecommendationManager.cs
│  │  └─ FeedVideoPlayerController.cs
│  └─ Editor/
│     └─ FeedRecommendationConfigEditor.cs
├─ 项目适配层模板/
│  └─ FeedRecommendationProjectAdapter.cs.txt
├─ 服务器示例/
│  ├─ feed-config.example.json
│  └─ nginx-location.conf
├─ DYADSS接入示例.md
└─ README.md
```

## 组件职责

### FeedRecommendationConfig

- 保存远程配置 URL、请求超时和本地兜底条目。
- 按“用户渠道 + ContentID”查找本地配置。
- 生成用于服务器部署的紧凑 JSON。

### FeedRecommendationManager

- 作为常驻单例管理推荐流启动流程。
- 校验场景标识并读取启动参数。
- 注册本包唯一的 `TT.OnFeedStatusChange` 监听。
- 优先请求远程配置，失败时读取本地配置。
- 触发准备、进入、退出和失败事件。
- 调用 `TT.ReportScene` 并防止单次启动重复上报。

### FeedVideoPlayerController

- 创建全屏视频画布和 `VideoPlayer`。
- 循环播放远程视频。
- 以视频首帧作为准备完成标准。
- 发生播放错误或 8 秒内无首帧时通知适配层。

### FeedRecommendationConfigEditor

- 在配置资产 Inspector 中生成服务器 JSON。
- 一键复制 JSON。
- 检查同一渠道下重复的 ContentID。

### FeedRecommendationDebuggerWindow

进入 Play Mode 后，从菜单 `抖音 -> 推荐流调试器` 打开。选择一条配置后可分别模拟
`SchemePrepared`、卡片 `OnShow/OnHide`、`FeedEnter/FeedExit`，直接调试项目适配代码。编辑器里的
`ReportSceneReady()` 只打印模拟日志，不调用抖音平台接口；调试器不进入正式构建。

### 项目适配层模板

- 把通用事件连接到目标游戏的关卡、UI 和兜底页面。
- 文件使用 `.cs.txt` 扩展名，避免复制前被误编译。
- 模板中的所有 TODO 都必须由接入项目实现。

## 当前实现与限制

| 项目 | 当前状态 |
| --- | --- |
| 获客渠道 `feed_game_channel=2` | 可解析并按 ContentID 路由 |
| 复访渠道 `feed_game_channel=1` | 支持全场景订阅，并通过远程配置动态上报 `StoreFeedData` |
| 视频/试玩/新手引导 | 提供统一方案枚举和项目事件 |
| 远程配置 | 支持 HTTP GET、超时、解析失败回退 |
| 本地兜底 | 支持 `Resources` 配置资产 |
| `feed_game_extra` 路由 | 尚未实现 |
| 场景就绪上报 | 支持真实耗时和单次防重 |
| 卡片划到/划走 | `CardShown/CardHidden` 对应 `OnShow/OnHide` |
| Feed 首次进入 | `FeedEntered` 与一次性 `SchemeEntered` |
| Feed 退出/再次进入 | `FeedExited/FeedEntered`，与卡片可见性分开 |
| 监听注销 | 保存委托并注销 Feed 与 App 生命周期监听 |
| 存档、体力、奖励、计时 | 不处理，由项目适配层控制 |

当前代码使用场景字符串以 `3041` 结尾作为推荐流判断。

## Feed 直玩多端适配（v1.0.13）

依据交付方提供的《抖音小游戏 Feed 直玩多端适配指南》截图（更新时间 2026-09-18）落实以下约束：

- 使用 `FeedRecommendationManager.IsDirectPlayScene(scene)` 统一判断场景后缀 `3041`，兼容 `023041`、`103041` 及未来宿主前缀；不要改成这两个完整场景值的白名单。
- `ReportSceneReady()` 的 `7001` 上报不得被 `hostName == douyin`、`Contains("douyin")`、`IsDouyin` 或同类宿主判断拦截。目标项目已有 Bootstrap、广告管理器和项目适配层也要移除这类针对直玩上报的外层限制。
- 多端适配不取消有效启动参数检查：仍需合法的 `feed_game_channel` 和非空 `feed_game_content_id`，普通入口继续走原游戏流程。
- 不调整隐藏预加载、OnShow/OnHide、FeedEnter/FeedExit 的边界；就绪上报仍在可交互且首帧已渲染后触发，每次启动防重。
- `993041` 仅作为未知宿主前缀的测试数据，不代表平台已开放该宿主。实际开放宿主以平台后台为准。

接入其他游戏时，先在普通入口、023041、103041 和已开放的其他宿主中验证启动分流，再确认各宿主均可收到 7001，并检查卡片显隐与进入完整游戏的生命周期。Unity 编辑器只模拟上报，不能替代开发者工具或真机验收。

## 拖入即用

1. 导入完整 `DouyinAdEnergySystem` UnityPackage。
2. 把 `DouyinAdSystem.prefab` 拖入首场景；不要再创建第二套 `TT.InitSDK`。
3. 在随包的 `Resources/FeedRecommendationConfig.asset` 中填写获客和复访 ContentID。
4. 胜利按钮绑定 `AdManager.RequestFeedSubscriptionOnWin()`，失败按钮绑定 `AdManager.RequestFeedSubscriptionOnFailure()`；两者共用每次启动一次的全场景订阅门闩。
5. 试玩关卡若需要目标游戏专属加载，复制项目适配层模板并实现对应 TODO；视频方案和当前场景安全兜底由默认适配层直接处理。

## 导入步骤

### 1. 确认唯一 SDK 初始化入口

目标项目原有的 `DYADSS`、`AdManager` 或 SDK Bootstrap 应继续作为唯一 `TT.InitSDK` 所有者。不要在 `FeedRecommendationManager` 中再次初始化 SDK。

### 2. 复制代码

把本目录中的：

```text
Assets/抖音/推荐流/
```

复制到目标项目对应的 `Assets/抖音/推荐流/`。

### 3. 创建配置资产

在 Unity 中选择：

```text
Assets → Create → 抖音 → 推荐流配置
```

保存路径必须为：

```text
Assets/Resources/FeedRecommendationConfig.asset
```

运行时按该名称通过 `Resources.Load` 查找配置。

### 4. 填写配置

可填写固定的远程 HTTPS 地址，例如：

```text
https://example.com/config/feed-game.json
```

本地条目字段：

| 字段 | 说明 |
| --- | --- |
| `contentId` | 平台下发的推荐内容 ID |
| `channel` | `ReturnVisit=1`、`Acquisition=2` |
| `returnSceneId` | 复访场景：`1` 离线收益、`2` 体力恢复、`3` 重要事件提醒；获客填 `0` |
| `scheme` | `Video=1`、`TrialLevel=2`、`NewPlayerGuide=3` |
| `payload` | 视频 URL、试玩 ID 或引导 ID，由项目解释 |
| `description` | 本地备注，不写入服务器 JSON |

### 5. 实现项目适配层

复制 `项目适配层模板/FeedRecommendationProjectAdapter.cs.txt` 到目标项目并改名为 `.cs`。至少实现：

- `SchemePrepared`：只预加载目标关卡、UI、输入和资源。
- `CardShown/CardHidden`：开始/暂停卡片 AI、配音、字幕和试玩计时。
- `SchemeEntered`：首次进入完整小游戏后执行登录、active、存档和正式承接。
- `FeedEntered/FeedExited`：重复进入/退出完整小游戏时恢复或暂停。
- `ConfigurationFailed`：加载始终可进入的安全兜底页面。
- 视频结束或关闭后的正式落地页。
- 未知 ContentID 的业务处理策略。

随包默认适配层已经订阅这些事件；替换为目标游戏适配层时仍应复用项目自身暂停系统，避免覆盖广告或菜单状态。

### 6. 接入现有启动流程

在唯一的 TTSDK 初始化成功回调中：

```csharp
TT.InitSDK((code, env) =>
{
    LaunchOption options = TT.GetLaunchOptionsSync();

    // 先注册业务事件，避免遗漏快速回调。
    MyGameFeedAdapter.Register();

    bool isFeedLaunch = FeedRecommendationManager.Instance
        .HandleLaunchOptions(options);

    if (!isFeedLaunch)
    {
        // 保留项目原有的普通启动流程。
    }
});
```

如果项目已有初始化器，只添加回调内部逻辑，不要复制第二个 `TT.InitSDK`。已有 `TT.OnFeedStatusChange` 或 `TT.ReportScene` 逻辑也需要统一所有者，避免重复监听和上报。

## 远程配置

服务器 JSON 只包含运行必需字段：

```json
{
  "items": [
    { "id": "CONTENT_VIDEO", "c": 2, "r": 0, "s": 1, "p": "https://example.com/video.mp4" },
    { "id": "CONTENT_RETURN", "c": 1, "r": 1, "s": 2, "p": "trial_level_3" }
  ]
}
```

- `id`：ContentID。
- `c`：渠道枚举值。
- `r`：复访场景 ID；获客为 `0`，复访必须为 `1`、`2` 或 `3`。
- `s`：体验方案枚举值。
- `p`：项目业务参数。

选中 `FeedRecommendationConfig.asset`，点击 Inspector 顶部“生成并复制”即可获得 JSON。服务器需要返回正确的 JSON Content-Type，并允许小游戏请求该地址；可参考 `服务器示例/nginx-location.conf`。

## 正确生命周期

```text
TTSDK 初始化完成
→ 项目读取 LaunchOption
→ 注册项目适配层
→ HandleLaunchOptions
→ 推荐流参数有效：注册 Feed 监听并解析配置
→ 远程配置优先，本地 Asset 兜底
→ SchemePrepared
→ 项目只预加载场景、资源、UI 和输入
→ 至少渲染出首个可展示画面
→ ReportSceneReady(sceneId=7001)
→ 等待卡片 OnShow
→ CardShown：同一方法开始 AI、配音和字幕
→ OnHide：CardHidden 暂停卡片体验
→ 第一次 FeedEnter：进入完整小游戏并触发一次 SchemeEntered
→ FeedExit：退出完整小游戏回推荐流，等待实际 OnShow 决定卡片是否恢复
```

预加载期间，用户可能从未真正进入游戏。首次 `FeedEnter` 前不要：

- 保存正式进度或覆盖老玩家存档；
- 扣除体力、发放正式奖励；
- 启动关卡倒计时、AI 或剧情推进；
- 强制登录、创角或上报真实激活；
- 展示插屏、签到、订阅等干扰弹窗。

## ReportScene 使用要求

正常方案准备完成后调用：

```csharp
FeedRecommendationManager.Instance.ReportSceneReady();
```

远程和本地配置都失败时，先创建安全兜底页面，再调用：

```csharp
FeedRecommendationManager.Instance.ReportFallbackSceneReady();
```

调用时机必须满足：

- 目标资源已经加载；
- 关键 UI 已经创建；
- 输入系统已经准备；
- 首个可展示画面已经渲染；
- 每次启动只上报一次。

视频方案已使用首帧回调作为就绪标准；试玩和引导方案需要目标项目自行判断真正就绪时机。

## 试玩配音与字幕时序

资源加载、视频首帧准备和 `ReportSceneReady()` 都属于隐藏预加载，不能作为正式配音或字幕起点。正确顺序：

```text
关卡、相机、UI、输入准备完成
→ ReportSceneReady()
→ 卡片 OnShow / CardShown
→ 同一方法、同一帧 AudioSource.Play()、字幕时钟清零并显示首条字幕
→ OnHide / CardHidden 同步暂停
→ 再次 OnShow 从正确位置恢复
```

`FeedEnter` 表示点击进入完整小游戏，不是卡片划到；不能用它启动卡片配音。视频首帧可在隐藏阶段静音解码，但正式有声播放必须由 `CardShown` 放行。

预览交接还应注意：

- `FeedEnter` 前可隐藏顶部 HUD，`SchemeEntered` 后恢复。
- 自动演示只能瞄准，不能发射或消耗资源。
- 移动端 WebGL 首次真实触摸前，`Input.mousePosition` 可能为 `(0, 0)`。关闭自动瞄准后应保持最后朝向，等真实按下后再读取坐标，否则炮台可能默认低头。
- 过滤触发 FeedEnter 的同一手势产生的松手事件，避免误发第一炮。

## 复访就绪数据

当前模板以全场景订阅为主链。玩家在胜利或失败结算发起申请后，管理器使用 `type=play, allScene=true`，不传单独 `scene/contentIDs`，并立即为 Scene 1/2/3 写入 `StoreFeedData(Status=1)`；不检查体力、离线收益或重要事件是否真实发生。

订阅查询回包兼容 `subscribed/status/success` 并记录完整原始 JSON。`GetFeedData` 仅用于平台排障，不作为复访路由和场景加载前置条件。

## 多入口设计

当前包按 `feed_game_content_id` 配置多个入口，不应使用获客场景通常相同的 `feed_game_scene=0` 区分不同关卡。

如果后续扩展 `feed_game_extra`，建议使用白名单路由，并按以下优先级解析：

```text
feed_game_extra.route → ContentID 映射 → 安全默认入口
```

高等级试玩完成后的策略必须由项目显式决定，例如继续下一关、从正式第 1 关开始、返回原存档或进入主菜单。不要默认把试玩进度永久写入正式存档。

## 验收清单

- [ ] 目标项目只有一个 `TT.InitSDK` 所有者。
- [ ] 目标项目只有一个推荐流启动参数解析器和 Feed 监听所有者。
- [ ] 普通启动仍走原流程。
- [ ] 至少两个 ContentID 可以进入不同内容。
- [ ] 未知 ContentID 有安全回退。
- [ ] 远程超时、404、无效 JSON 时可回退本地配置。
- [ ] 预加载阶段不计时、不存档、不扣体力、不弹广告。
- [ ] 内容真正可展示后才调用 `ReportScene`。
- [ ] 每次启动最多调用一次 `ReportScene`。
- [ ] 隐藏预加载不启动 AI、配音、字幕或试玩计时。
- [ ] 首次 `OnShow` 启动卡片体验，`OnHide/OnShow` 可暂停恢复。
- [ ] `FeedEnter/FeedExit` 只处理完整小游戏进出。
- [ ] 胜利和失败入口都触发全场景订阅，且每次启动最多弹一次。
- [ ] 申请路径分别写入 Scene 1/2/3 的默认 `Status=1`。
- [ ] 远程和本地都失败时仍可进入安全页面并上报。
- [ ] 在完整目标 Unity 项目中编译通过。
- [ ] 完成普通启动、多个入口、未知入口、平台离线测试、风险检测和提审验证。

## 相关资料

- [DYADSS 接入示例](./DYADSS接入示例.md)
- [抖音开放平台：推荐流获客版接入指南](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/guide/open-ability/feed/customer-access-guide)
- [抖音开放平台：推荐流直玩 API](https://developer.open-douyin.com/docs/resource/zh-CN/mini-game/develop/api/c-api/tt-feed-game-api)

平台字段、客户端版本、基础库版本和 TTSDK 最低版本会变化，正式接入时请重新核对官方文档。

## 协作与提交

建议所有改动使用独立分支，通过 Pull Request 合并到保护分支：

1. 从最新 `master` 创建功能分支。
2. 修改代码并完成目标 Unity 项目的编译与真机验证。
3. 使用中文提交标题和中文提交说明。
4. 创建 Pull Request，并由非代码作者完成审核。
5. 审核通过后再合并，禁止直接推送保护分支。

仓库保护、审批人数和禁止自审需要在 Gitee 服务端单独配置，本仓库文件本身不能强制这些规则。
