# DouYinIconDemo

抖音广告系统集成模块，为其他项目提供标准化的广告和体力系统接口。

## 目录结构

```
DouYinIconDemo/
├── ADSScripts/
│   ├── AdManager.cs      # 业务公开 API
│   ├── AdManagerImpl.cs  # 抖音 SDK 实现层
│   ├── EnergySystem.cs   # 体力系统
│   ├── EnergyUI.cs       # UI示例
│   ├── SideBarManager.cs # 侧边栏入口与每日奖励
│   └── TTSaveManager.cs  # 本地存档
├── Image/                # 图标资源
├── Prefabs/              # 可选 UI 预制体
└── 推荐流通用接入包/       # 推荐流完整源码、配置、适配模板和服务器示例
```

`推荐流通用接入包` 会随 DouYinIconDemo 一起导出，其中保留 Core、Editor、项目适配层模板、
服务器 JSON/Nginx 示例和完整接入文档。目标项目可以直接引用通用类型，也可以复制模板后按自身
关卡、存档、登录和暂停系统进行适配。

## 使用方法

### 1. 广告系统 (AdManager)

`SampleScene` 中的 `DouyinAdSystem` 是独立场景根物体，同时挂载
`AdManager` 和 `AdManagerImpl`。不要把两个管理脚本挂到 Panel、Canvas
或其他可能被隐藏/销毁的 UI 物体上。该物体会进入 `DontDestroyOnLoad`，
且不持有任何场景 UI 引用。

### 原子按钮

`Prefabs/AtomicButtons` 提供四个可直接放入任意 Canvas、任意场景的按钮：

- `DouyinShareButton.prefab`：分享
- `DouyinAddDesktopButton.prefab`：添加桌面，完成后默认隐藏自身
- `DouyinSidebarButton.prefab`：直接打开抖音侧边栏
- `DouyinRankButton.prefab`：打开排行榜

也可以只把同名脚本添加到现有 `UnityEngine.UI.Button`。按钮在点击时调用
常驻 `AdManager`，自身随场景销毁不会影响广告系统。按钮的成功、失败、返回
事件都保留为自身 Inspector 中的 `UnityEvent`，不需要在 Button OnClick 中绑定
`AdManager`。

```csharp
// 播放激励视频广告，广告结束后回调
AdManager.Instance.ShowRewardVideoAd(() =>
{
    Debug.Log("广告观看完成，发放奖励");
}, () =>
{
    Debug.Log("广告未看完或播放失败，不发放奖励");
});

// 播放插屏广告
AdManager.Instance.ShowInterstitialAd();

// 当前规则：本次运行前30秒保护；真实插屏展示或激励关闭后，会话内60秒冷却
```

### 2. 体力系统 (EnergySystem)

```csharp
// 获取单例
var energy = EnergySystem.Instance;

// 获取当前体力显示文本
string display = energy.GetDisplayText();  // "10" 或 "∞"

// 获取恢复倒计时
string countdown = energy.GetCountdownText();  // "04:59"

// 尝试消耗体力开始游戏
if (energy.TryConsumeEnergy())
{
    // 体力充足，开始游戏
}

// 检查是否有体力
if (energy.HasEnergy())
{
    // 有体力
}

// 观看广告增加体力
energy.OnClickAddEnergy(() =>
{
    Debug.Log("体力已增加");
});
```

### 3. 依赖说明

ADSScripts 依赖:
- TTSDK 6.7.4
- UniTask
- `MyHelper.cs`
- `TTSaveManager.cs`
