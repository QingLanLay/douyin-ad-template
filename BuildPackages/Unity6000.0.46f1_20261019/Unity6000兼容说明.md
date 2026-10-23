# Unity 6000.0.46f1 专用完整模板包

版本：DouyinAdEnergySystem_v1.0.15_Unity6000.0.46f1_20261019
日期：2026-10-19

## 范围

基于当前模板 v1.0.14 的完整源码与资源，保留广告、体力、宝箱、原子按钮、侧边栏、推荐流、教程、ByteGame SDK、UniTask 和抖音字体。保留现用预制体、GUID、UnityEvent 和测试模式，不创建运行时替代 UI。

## 导入

1. 使用 Unity 6000.0.46f1，先备份目标项目。
2. 在 Package Manager 安装 Unity UI（com.unity.ugui）2.0.0。该版本内含 TextMesh Pro，不要额外安装旧版 com.unity.textmeshpro 3.x。
3. 通过 Window > TextMeshPro > Import TMP Essential Resources 导入当前 Unity 6 的 TMP 基础资源。模板包不夹带旧版 TMP 默认资源、Packages 或 ProjectSettings。
4. 导入完整 unitypackage；若已有更高版本 ByteGame SDK 或 UniTask，取消对应目录以避免重复依赖。
5. 将 Assets/DouYinIconDemo/Prefabs/Systems/DouyinAdSystem.prefab 放入启动场景；其他 UI 预制体按需直接放进 Canvas。
6. 替换目标项目自己的广告位、ContentID 和业务回调，发布前关闭 AdManager.isTest。

## 边界

此包仅面向 Unity 6000.0.46f1，不用于覆盖原 Unity 2021 工程。导入、编译与资源检查以随包交付的验证报告为准；抖音真实广告、添加桌面用户手势、Feed 及真机行为仍需平台验收。

## 历史示例例外

不包含 Assets/DouYinIconDemo/Image/Share.prefab：它在原工程中已引用不存在的 Spine 组件、骨骼和材质，属于失效旧分享示例，并非 Unity 6 迁移导致。现用分享入口 Assets/DouYinIconDemo/Prefabs/AtomicButtons/DouyinShareButton.prefab 保留，其余 12 个现用功能预制体一并保留。此例外仅用于本兼容包的导出清单，不修改或删除原工程文件。
