# DYADSS 接入示例

每个项目的 `DYADSS` 代码不同，本包不提供或覆盖该文件。

## 1. 保证正式包启动时创建 DYADSS

如果项目使用运行时单例，建议：

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
private static void BootstrapInPlayer()
{
#if !UNITY_EDITOR
    _ = DYADSS.Ins;
#endif
}
```

测试开关不要让正式包提前返回：

```csharp
#if UNITY_EDITOR
public bool isTest = true;
#else
public bool isTest = false;
#endif
```

## 2. TTSDK 初始化完成后接入

```csharp
TT.InitSDK((code, env) =>
{
    LaunchOption options = TT.GetLaunchOptionsSync();

    // 先注册当前游戏如何执行视频、试玩和新手引导。
    MyGameFeedAdapter.Register();

    bool isFeedLaunch = FeedRecommendationManager.Instance
        .HandleLaunchOptions(options);

    if (!isFeedLaunch)
    {
        // 当前项目原有的普通启动场景上报。
        启动场景数据上报();
    }
});
```

推荐流启动不要再由 `DYADSS` 重复注册 `TT.OnFeedStatusChange` 或提前调用 `TT.ReportScene`。

不要在 SDK 初始化、登录成功或页面自动打开时请求订阅。平台要求订阅弹窗由玩家点击行为触发，例如结算界面的“重玩”“选关”或“返回首页”：

```csharp
public void OnGameOverNavigationButtonClick()
{
    FeedRecommendationManager.Instance
        .TryRequestFeedSubscribeAllSceneFromUserGesture();

    // 原按钮的重玩或导航逻辑继续执行。
}
```

该入口每次运行最多尝试一次。全场景订阅参数由管理器统一设置为 `type=play, allScene=true`，不传 `scene` 或 `contentIDs`。查询失败时不会继续弹出订阅请求，避免在平台能力异常时打扰用户。

## 3. 项目适配层职责

参考 `项目适配层模板/FeedRecommendationProjectAdapter.cs.txt`：

- 订阅 `SchemePrepared` 并预加载业务内容。
- 内容真正准备完成后调用 `ReportSceneReady()`。
- 订阅 `CardShown/CardHidden`，只在卡片 `OnShow` 后开始配音、字幕和 AI，并在 `OnHide` 暂停。
- 订阅 `SchemeEntered` 执行首次进入完整小游戏后的动作。
- 订阅 `FeedEntered/FeedExited` 处理完整小游戏重复进入和退出。
- 订阅 `ConfigurationFailed`，加载首页等安全兜底内容并上报。

不要在通用 Core 中引用具体游戏脚本。
