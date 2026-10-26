# 抖音 Unity2022 → Unity6000 随包自动兼容

## 日常使用

- 在 Unity 2022.3.62f1（含本机 c1）维护模板、SDK和本插件；导出业务 unitypackage 时必须同时选择整个 `Assets/Editor/DouyinUnity6000Compat` 和 `Assets/Plugins/WebGL` 下两份 `DouyinUnity6*.jslib`，保留 `.meta` / GUID。
- 在目标 Unity 6000.0.46f1 工程导入业务包后，继续使用抖音 SDK 原面板手动构建或“仅转换”，无需让 Codex 每次逐项改导出。
- 保持 Editor 开启，构建/转换结束且目标文件连续稳定后，后台自动检查并完成兼容。Unity原生构建回调仅标记等待，不能冒充SDK最终转换完成；不要在Console完成提示前立即上传或关闭Editor。
- `Tools/抖音小游戏/6000自动兼容` 可查看状态、指定SDK改版后的当前输出/转换源路径、重检及导出独立插件包。

## 版本边界

- 不锁定 SDK / UnityPlugin 版本，不附带、不升级、不修改官方SDK。检测到的版本仅写报告；目录和framework结构发生变化时停止、保留文件，依据新SDK更新插件和Skill后再继续。
- 2022中可编译、修改和导出。Editor服务不处理2022产物，构建回调不阻止2022构建；jslib用PluginImporter定义约束排除2022。导入时保持WebGL专用设置和meta，不要勾选Any Platform。
- 桥接目标是6000.0.46f1，不是“把2022编译好的Wasm改成6000”。2022导出的源资源包由6000正常导入/编译；不伪造ProjectVersion/unityVersion，不自动迁移或降级整个项目。
- 6000.0以外的Unity不链接本jslib；6000.0其他补丁版本也不自动收尾，隐式退出包装在运行时只对精确目标版本生效。未验证其他补丁版本。

## 实际作用

文件访问层只在缺失时补回doAccess，保留权限、路径错误和负errno；隐式退出层只处理目标抖音保活运行时正常main返回，不吞显式退出、非零错误或真正运行时关闭。自动收尾同时覆盖最终framework和能够确认的WebGL转换源；无法定位源时只处理最终包并在报告注明，使用“仅转换”前应指定实际源文件。

不改AppID、SDK DLL/缓存、Wasm、AB、存档、游戏资源或上传忽略规则，也不硬编码平台包体上限。未知结构停止，并不代表未知SDK被自动验证通过。

每次处理的SHA-256、逐文件原件及结果位于 `.artifacts/DouyinAutoCompat/Runs/`。回退前逐项核对当前哈希仍等于afterSha256，再用对应backup恢复；不覆盖后续SDK重建或用户编辑。自动收尾失败时仅回退仍匹配本轮写入的文件。

工具/结构/Editor编译检查与完整SDK构建、抖音启动、真机、发布审核分别验收。插件成功提示不代表后几项通过。