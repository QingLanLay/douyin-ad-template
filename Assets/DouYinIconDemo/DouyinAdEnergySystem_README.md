# 抖音广告与体力原子系统

## 包含内容

- 常驻 `DouyinAdSystem` Prefab：`AdManager`、`AdManagerImpl`、`EnergySystem` 位于同一个 GameObject。
- 原子按钮 Prefab：分享、添加桌面、侧边栏、排行榜。
- 侧边栏教学与领奖面板 Prefab：`SiderBarPanel`。
- 原子体力 Prefab：体力状态入口、体力面板、扣体力测试按钮。
- 当前项目使用的抖音 ByteGame SDK、UniTask、抖音字体和 UI 图片。
- 推荐流通用接入包完整源码、配置资产、项目适配模板、服务器示例和接入文档。

## 未包含内容

- TextMesh Pro 默认资源与 Package；目标项目需要已经安装 TextMesh Pro。
- `SampleScene` 等示例场景。
- 目标项目自己的广告位、推荐流 content_id 和业务回调配置。

## 导入注意事项

1. 建议先备份目标项目。
2. 如果目标项目已经安装了更新版本的 ByteGame SDK 或 UniTask，请在 Unity 的导入窗口中取消勾选：
   - `Assets/Plugins/ByteGame`
   - `Assets/UniTask`
3. 将 `Assets/DouYinIconDemo/Prefabs/Systems/DouyinAdSystem.prefab` 放入首个启动场景一次。
4. 在 `DouyinAdSystem` 上填写目标应用自己的激励广告位、插屏广告位和推荐流 content_id。
5. 发布前关闭 `AdManager.isTest`；包内保留模板当前的测试模式，避免导入后误请求真实广告。
6. 原子按钮可以直接放到任意 Canvas。体力状态入口会查找当前已加载场景中的 `EnergyPanelView`，体力面板可以放在同一场景的任意 Canvas 下。
7. `SiderBarPanel.prefab` 可以直接放到任意 Canvas。`DouyinSidebarButton.prefab` 点击后只激活该面板，不会直接调用平台 API；真正的侧边栏 API 仅由面板内部的“进入侧边栏”按钮调用。侧边栏奖励按本地自然日记录，每天最多领取一次。
8. `DouyinAddDesktopButton.prefab` 和 `DouyinSidebarButton.prefab` 不会再根据已添加或已领取状态隐藏自身；业务层可通过各自的 UnityEvent 显示提示。
9. 推荐流源码位于 `Assets/DouYinIconDemo/推荐流通用接入包`。导入后先阅读其中的 `README.md`，
   保证目标项目只有一个 `TT.InitSDK` 和一个 Feed 监听所有者，再填写
   `Resources/FeedRecommendationConfig.asset` 中的 ContentID 或远程 JSON 地址。

## 运行要求

- 建议 Unity 2021.3 或更高版本。
- 需要 uGUI 和 TextMesh Pro。
- 真实广告、加桌、侧边栏和排行榜必须在抖音开发者工具或真机中验证。

## 广告请求规则

- 抖音插屏在本次运行前 30 秒内禁止展示。
- 创建、加载、调用 `Show()`、`OnError` 或 `Show` 异常均不重置插屏 CD；TTSDK 没有 `OnShow`，只有收到 `OnClose`、确认插屏确实展示并关闭后，才开始本次运行内的 60 秒插屏 CD。
- 任意激励广告关闭后，本次运行内 60 秒不得展示插屏；该间隔不限制激励广告。
- 插屏间隔使用 `Time.realtimeSinceStartup` 保存在进程内，不写入 PlayerPrefs 或游戏存档。
- 插屏实例未创建或未加载完成时只创建/加载，不消耗 60 秒间隔。
- 插屏 `OnError` 或 `Show` 异常后会清理当前实例并停止自动申请；只有业务下次主动调用插屏接口时才重新申请一次。
- 激励广告按钮不得因为请求处理中而锁定；后一次点击替换旧业务回调并再次请求。
- 激励奖励只在 SDK 返回完整观看时发放，并通过请求编号隔离过期回调和重复奖励。
