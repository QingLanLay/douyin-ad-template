# 资源迁移指南

## 需要的图片资源

从 `Assets/抖音/体力系统/` 移动以下文件到 `Assets/DouYinIconDemo/Icons/`:

| 文件 | 说明 |
|------|------|
| `体力.png` | 体力图标 |

## 移动 Prefab 资源

1. 体力UI Prefab: `Assets/抖音/体力系统/体力相关.prefab` → `Assets/DouYinIconDemo/Prefabs/`
2. 分享按钮: `Assets/抖音/分享有奖/` → `Assets/DouYinIconDemo/Prefabs/`
3. 文字提示: `Assets/抖音/文字提示/` → `Assets/DouYinIconDemo/Prefabs/`

## 依赖关系

新脚本依赖:
- `Assets/DouYinIconDemo/Scripts/Helper/Helper.cs` - 工具类
- `Assets/DouYinIconDemo/Scripts/Storage/Storage.cs` - 存储接口

可选依赖（用于实际SDK集成）:
- `Assets/DouYinIconDemo/Scripts/AdManager.cs` 中的 `DYADSSBridge` 类需要替换为实际抖音SDK实现

## 删除旧文件

清理完成后，可以安全删除 `Assets/抖音/` 文件夹。
