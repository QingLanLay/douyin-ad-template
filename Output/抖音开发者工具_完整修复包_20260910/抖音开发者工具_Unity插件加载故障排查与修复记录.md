# 抖音开发者工具 Unity 插件加载故障排查与修复记录

记录日期：2026-09-10

## 一、问题现象

同一游戏导出包在正常电脑可以打开，在同事电脑启动失败，最初报错：

```text
Can't find module __plugin__tt674511f8f84ea91612/index.js
at main(game.js:45)
```

对应启动代码：

```javascript
const UnityManager = requirePlugin('UnityPlugin/index.js');
```

项目声明的插件配置：

```json
{
  "plugins": {
    "UnityPlugin": {
      "version": "4.32.0",
      "provider": "tt674511f8f84ea91612"
    }
  }
}
```

`tt674511f8f84ea91612` 是抖音 Unity 引擎插件的提供方 ID，不是开发者账号 ID，也不是游戏 AppID。插件负责 Unity 游戏在抖音小游戏环境中的运行支持。

## 二、初步排查：查找并补充插件缓存

在正常电脑找到插件缓存：

```text
%APPDATA%\@byted\vela\tempFile\pc-sdk\micro-plugin-packages\tt674511f8f84ea91612\online
```

目录包含：

```text
index.js
index.js.LICENSE.txt
plugin.config.json
signature.json
```

其中 `index.js` 为 946023 字节，配置版本为 `4.32.0`。将上述文件连同目录结构打包，供同事尝试恢复缓存。

**这一步不是最终确认有效的修复。** 后续对比证明同事的插件四个文件与正常电脑一致，无需重复复制插件。

## 三、对比开发者工具安装目录

检查同事提供的 `@bytedminiprogram-ide.7z`，两台电脑的工具版本均为：

```text
版本：4.5.6
构建号：478346290
```

该压缩包是工具安装目录，不包含用户 AppData 下的运行缓存。安装目录里没有 UnityPlugin 缓存，不能证明同事电脑没有下载插件。

## 四、对比完整故障资料

继续检查同事提供的 `抖音工具加载故障对比_20260910_142418.7z`，并与正常电脑实际文件比对。

| 项目 | 正常电脑 | 故障电脑 | 判断 |
|---|---|---|---|
| 开发者工具版本 | 4.5.6 | 4.5.6 | 一致 |
| 主程序 SHA256 | 与故障电脑相同 | 与正常电脑相同 | 已核验主程序内容一致，不代表全部安装文件均已逐项比对 |
| UnityPlugin | 4.32.0 | 4.32.0 | 四个缓存文件 SHA256 全部一致 |
| 基础库 | 正常日志显示 4.27.0.1 | 采集包包含 4.29.0.3 快照 | 值得优先对齐；快照本身不证明当时实际使用版本 |
| Windows | Build 26200 | Windows 10，Build 19045 | 有差异，但没有证据证明是原因 |

主程序 SHA256：

```text
BCC8223BFE5FFE26EE51E8B6268670D0E11845AEB03127C2804710D7A7912C7A
```

随包记录还说明，故障电脑曾备份重置 `vela` 用户环境并重新下载插件，但仍然失败。

### 关于 inject success 日志

故障日志包含：

```text
[TMG] plugin tt674511f8f84ea91612 inject success!
```

检查采集包中 `4.29.0.3` 基础库源码发现，这条日志在真正调用插件加载函数之前打印，不能证明插件执行或模块注册已经成功。后续 `Can't find module` 表示解析入口时仍未找到模块。

另一次运行曾出现 `afterLoadGameJS is not defined`，但没有证据证明与当前错误同因，不应通过补空函数来掩盖报错。

## 五、对齐基础库后出现的新错误

建议同事将调试基础库切换到正常日志使用的 `4.27.0.1`，UnityPlugin 仍保持 `4.32.0`。这两个版本分别属于基础库和 Unity 插件，不应混淆。

切换后出现：

```text
Refused to execute script from
http://127.0.0.1:7044/core/4.27.0.1/tmg-core.js
because its MIME type ('text/html') is not executable,
and strict MIME type checking is enabled.
```

同时出现：

```text
sdkVersion=NaN.NaN.NaN.NaN
Unity 插件仅支持基础库大于等于 3.77.0 版本，
当前选择基础库版本 NaN.NaN.NaN.NaN
```

该错误说明工具本地服务没有以可执行 JavaScript 类型提供基础库脚本，浏览器拒绝执行；版本没有正常识别，继而出现 `NaN`。

`NaN` 不表示 `4.27.0.1` 低于最低要求。由于没有取得请求的原始状态码和响应正文，不能进一步断言一定是下载文件损坏或具体路由故障。

## 六、最终修复步骤（用户确认有效）

1. 完全退出抖音开发者工具。
2. 在资源管理器地址栏打开：

   ```text
   %APPDATA%\@byted\vela\tempFile\sdk\microgame
   ```

3. 将已有的 `4.27.0.1` 文件夹改名为 `4.27.0.1.bak`，保留备份。如果同名备份已经存在，使用带时间戳的备份名，避免覆盖。
4. 重新打开开发者工具，重新选择并下载基础库 `4.27.0.1`。
5. 等待下载完成，重新编译游戏。

**用户执行后反馈“这样搞可以修复”。** 因此本次确认有效的操作是备份并重建对应版本的基础库缓存。

## 七、结果与复用范围

完整流程：

```text
插件模块找不到
  → 查找并提供正常插件缓存
  → 对比两机工具主程序和插件文件，确认一致
  → 优先对齐基础库到 4.27.0.1
  → 出现基础库脚本 MIME 错误及 NaN 版本
  → 备份该版本缓存并重新下载
  → 用户确认可以修复
```

本次没有通过修改 Unity 广告业务、Unity 中的抖音 SDK、插件 ID 或 MIME 检查解决问题。

修复结果来自用户确认，未取得故障电脑修复后的完整运行日志。不能把所有 `Can't find module` 都归结为同一个原因，也不能把复制插件或重装同版本工具记录为本次已验证有效的办法。

如果相同步骤仍无效，应保存基础库脚本请求的状态码、Response 内容以及完整启动 Console，再进一步定位本地服务为什么没有正确提供脚本。
