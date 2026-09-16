# 原子化宝箱按钮与弹窗

Prefab：`TreasureChestRewardWidget.prefab`

## 使用

1. 把 Prefab 直接拖入任意场景，不需要额外 Canvas。
2. `Popup Mode` 选择弹出方式：
   - `Manual Click`：倒计时结束后显示“获取宝箱”，玩家点击入口打开。
   - `Auto On Countdown Complete`：倒计时结束后自动打开。
3. `Countdown Seconds` 默认为 35 秒，可在 Inspector 直接修改。
4. 把实际奖励逻辑绑到 `On Reward 1 Selected` 和 `On Reward 2 Selected`。

## 行为

- 倒计时文字格式为 `0:35s`，完成后显示“获取宝箱”。
- Panel 打开时记录当前 `Time.timeScale` 并暂停游戏，关闭时恢复原值。
- 奖励选择后默认关闭 Panel 并重新开始 35 秒倒计时。
- Prefab 自带备用 EventSystem；场景已有 EventSystem 时不会启用备用对象。
