#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DouyinUnity6000Compat
{
    /// <summary>可在2022维护和导出；只对目标Unity版本的抖音产物自动收尾，不引用SDK程序集。</summary>
    [InitializeOnLoad]
    public static class DouyinCompatService
    {
        public const string UnityVersion = "6000.0.46f1";
        private const string PluginFolder = "Assets/Editor/DouyinUnity6000Compat";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly DouyinCompatWatchGate Watch = new DouyinCompatWatchGate();
        private static double nextPoll;
        private static string status = "2022可维护并导出；在Unity 6000.0.46f1中等待SDK最终产物稳定后自动兼容。";
        public static bool IsBusy { get; private set; }
        public static bool IsTargetEditor => Application.unityVersion == UnityVersion;
        private static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string ArtifactRoot => Path.Combine(Root, ".artifacts/DouyinAutoCompat");
        private static string PreferencePrefix => "Douyin6000Compat." + Hash(Utf8.GetBytes(Root)).Substring(0, 16) + ".";
        public static bool AutomaticEnabled
        {
            get => EditorPrefs.GetBool(PreferencePrefix + "automatic", true);
            set { EditorPrefs.SetBool(PreferencePrefix + "automatic", value); Watch.Reset(); status = value ? "自动兼容已开启；仅在目标Unity版本生效。" : "自动兼容已关闭。"; }
        }
        // SDK设置字段/目录未来变化时由用户明确指定，不扫描或批量修改历史导出。
        public static string OutputOverride
        {
            get => EditorPrefs.GetString(PreferencePrefix + "output", string.Empty);
            set { EditorPrefs.SetString(PreferencePrefix + "output", value ?? ""); Watch.Reset(); }
        }
        public static string SourceOverride
        {
            get => EditorPrefs.GetString(PreferencePrefix + "source", string.Empty);
            set { EditorPrefs.SetString(PreferencePrefix + "source", value ?? ""); Watch.Reset(); }
        }
        public static string LatestReportPath => EditorPrefs.GetString(PreferencePrefix + "report", string.Empty);
        public static string Status => status;
        [Serializable] private sealed class PackageInfo { public string version = null; }
        [Serializable] private sealed class SdkPlugins { public Plugin UnityPlugin = null; }
        [Serializable] private sealed class Plugin { public string version = null; }
        [Serializable] private sealed class GameInfo { public SdkPlugins plugins = null; }
        [Serializable] private sealed class ProjectInfo { public string appid = null; }
        [Serializable] public sealed class FileChange
        { public string path, beforeSha256, afterSha256, backup; public bool changed, written, rolledBack; }
        [Serializable] public sealed class Result
        {
            public string createdUtc, project, output, source, trigger, unity, sdk, plugin, message;
            public bool success, compatibilityPassed, sourceProcessed, completeGameLoadVerified, deviceVerified;
            public FileChange[] files;
        }
        private sealed class WritePlan { public string Path; public byte[] Before, After; }
        static DouyinCompatService()
        {
            if (!AssetDatabase.IsAssetImportWorkerProcess()) EditorApplication.update += Update;
        }
        private static string Setting(string name)
        {
            var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Editor/StarkBuilderSetting.asset");
            if (settings == null)
                foreach (string guid in AssetDatabase.FindAssets("t:StarkBuilderSettings"))
                {
                    settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
                    if (settings != null) break;
                }
            if (settings == null) return null;
            using (var data = new SerializedObject(settings))
            {
                var property = data.FindProperty(name);
                return property != null && property.propertyType == SerializedPropertyType.String ? property.stringValue : null;
            }
        }
        public static string DetectSdkVersion()
        {
            foreach (string file in new[] {
                Path.Combine(Root, "Assets/Plugins/ByteGame/com.bytedance.starksdk/package.json"),
                Path.Combine(Root, "Packages/com.bytedance.starksdk/package.json") })
                if (File.Exists(file))
                    try { return JsonUtility.FromJson<PackageInfo>(File.ReadAllText(file))?.version ?? "unknown"; } catch { }
            return "unknown"; // 仅供报告，不作为版本准入条件。
        }
        public static string ResolveOutputDirectory()
        {
            string configured = string.IsNullOrWhiteSpace(OutputOverride) ? Setting("OutputDir") : OutputOverride;
            if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("无法读取SDK导出目录，请在兼容窗口指定当前小游戏目录。");
            string parent = InsideProject(Path.IsPathRooted(configured) ? configured : Path.Combine(Root, configured));
            if (!string.IsNullOrWhiteSpace(OutputOverride)) return parent;
            return File.Exists(Path.Combine(parent, "game.json")) ? parent : InsideProject(Path.Combine(parent, "tt-minigame"));
        }
        private static string SourceFramework(string output)
        {
            if (!string.IsNullOrWhiteSpace(SourceOverride))
                return InsideProject(Path.IsPathRooted(SourceOverride) ? SourceOverride : Path.Combine(Root, SourceOverride));
            string known = InsideProject(Path.Combine(Directory.GetParent(output).FullName, "webgl/Build/webgl.framework.js"));
            return File.Exists(known) ? known : null;
        }
        public static void ValidateBeforeBuild()
        {
            if (!IsTargetEditor) return; // 2022及非目标版本正常构建，不由本插件阻断。
            foreach (string file in new[] { "DouyinUnity6FileAccess.jslib", "DouyinUnity6ImplicitExit.jslib" })
                if (!File.Exists(Path.Combine(Application.dataPath, "Plugins/WebGL", file)))
                    throw new BuildFailedException("兼容源码缺失，请重新导入完整插件：" + file);
        }
        private static void ValidateOutput(string output)
        {
            if (!IsTargetEditor) throw new InvalidOperationException("当前为维护或非目标版本；仅Unity " + UnityVersion + "执行产物兼容。");
            ValidateBeforeBuild();
            foreach (string file in new[] { "game.js", "game.json", "project.config.json", "unity-namespace.js", "webgl.framework.js" })
                if (!File.Exists(Path.Combine(output, file))) throw new InvalidOperationException("导出尚未完整或SDK布局变化，缺少：" + file);
            string names = File.ReadAllText(Path.Combine(output, "unity-namespace.js"));
            var version = Regex.Match(names, "unityVersion\\s*:\\s*['\"]([^'\"]+)['\"]");
            if (!version.Success || version.Groups[1].Value != UnityVersion)
                throw new InvalidOperationException("导出包Unity版本不是目标版本；不会改版本号。");
            var config = JsonUtility.FromJson<ProjectInfo>(File.ReadAllText(Path.Combine(output, "project.config.json")));
            string currentAppId = Setting("_appId");
            if (string.IsNullOrWhiteSpace(config?.appid) || (!string.IsNullOrWhiteSpace(currentAppId) && config.appid != currentAppId))
                throw new InvalidOperationException("导出包AppID为空或与当前SDK配置不同；未修改其他游戏。");
        }
        public static void RunCurrentExport()
        {
            if (IsBusy || BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("正在构建、编译或运行，请结束后检查。");
            string output = ResolveOutputDirectory();
            ProcessOutput(output, SourceFramework(output), "manual");
            Watch.MarkProcessed(Signature(output));
        }
        private static void Update()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 2d;
            ReadCommand();
            if (!IsTargetEditor || !AutomaticEnabled || IsBusy) return;
            bool busy = BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorUtility.scriptCompilationFailed || EditorApplication.isPlayingOrWillChangePlaymode;
            if (busy) { Watch.Observe(null, EditorApplication.timeSinceStartup, true); return; }
            string output, signature;
            try { output = ResolveOutputDirectory(); signature = Signature(output); } catch { return; }
            if (!Watch.Observe(signature, EditorApplication.timeSinceStartup, false)) return;
            try { ProcessOutput(output, SourceFramework(output), "stable-sdk-output"); }
            catch (Exception exception) { Debug.LogError("[抖音自动兼容] " + exception.Message); }
            finally { try { Watch.MarkProcessed(Signature(output)); } catch { } }
        }
        public static void MarkWebGLBuildCompleted()
        { Watch.Reset(); status = "WebGL构建结束，等待SDK最终转换稳定后自动兼容；现在不改中间文件。"; }
        private static string Signature(string output)
        {
            if (!Directory.Exists(output) || !File.Exists(Path.Combine(output, "game.json"))) return null;
            var text = new StringBuilder(output);
            foreach (string file in new[] { "game.js", "game.json", "project.config.json", "unity-namespace.js", "webgl.framework.js" })
            {
                var info = new FileInfo(Path.Combine(output, file));
                if (!info.Exists) return null;
                text.Append('|').Append(file).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks);
            }
            string source = SourceFramework(output);
            if (source != null && File.Exists(source)) { var info = new FileInfo(source); text.Append('|').Append(source).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks); }
            return Hash(Utf8.GetBytes(text.ToString()));
        }
        internal static Result ProcessOutput(string output, string source, string trigger)
        {
            output = InsideProject(output);
            if (source != null) source = InsideProject(source);
            if (IsBusy) throw new InvalidOperationException("已有自动兼容任务。");
            string run = Path.Combine(ArtifactRoot, "Runs", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(run); IsBusy = true;
            var result = new Result { createdUtc = DateTime.UtcNow.ToString("o"), project = Root, output = output, source = source, trigger = trigger,
                unity = Application.unityVersion, sdk = DetectSdkVersion(), plugin = "unknown" };
            var applied = new List<WritePlan>();
            try
            {
                ValidateOutput(output);
                result.plugin = JsonUtility.FromJson<GameInfo>(File.ReadAllText(Path.Combine(output, "game.json")))?.plugins?.UnityPlugin?.version ?? "unknown";
                if (source != null && !File.Exists(source)) throw new InvalidOperationException("指定的WebGL转换源不存在，未修改任何文件。");
                string finalFramework = InsideProject(Path.Combine(output, "webgl.framework.js"));
                string[] targets = source == null ? new[] { finalFramework } : new[] { finalFramework, source };
                var plans = new List<WritePlan>();
                foreach (string file in targets.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    byte[] before = File.ReadAllBytes(file);
                    var patch = DouyinCompatCore.Plan(Utf8.GetString(before));
                    plans.Add(new WritePlan { Path = file, Before = before, After = Utf8.GetBytes(patch.Text) });
                }
                // 所有framework均通过结构检查才写入；不改SDK/上传配置/版本号/资源。
                var changes = plans.Select(plan => new FileChange { path = plan.Path, beforeSha256 = Hash(plan.Before), afterSha256 = Hash(plan.After), changed = !plan.Before.SequenceEqual(plan.After) }).ToArray();
                result.files = changes;
                for (int i = 0; i < plans.Count; i++)
                {
                    var plan = plans[i]; var change = changes[i];
                    if (!change.changed) continue;
                    change.backup = Path.Combine(run, i.ToString("00") + ".before");
                    File.WriteAllBytes(change.backup, plan.Before);
                    if (Hash(File.ReadAllBytes(plan.Path)) != change.beforeSha256) throw new IOException("SDK输出又变化，请等待转换完成。");
                    WriteAtomic(plan.Path, plan.After); change.written = true; applied.Add(plan);
                }
                result.success = result.compatibilityPassed = true;
                result.sourceProcessed = source != null;
                result.message = "已完成已知桥接结构兼容；SDK/UnityPlugin版本仅记录，不代表所有SDK均通过平台验收。" +
                    (source == null ? " 未发现转换源；仅处理最终包，若使用仅转换请指定实际源framework。" : " 已同步实际WebGL转换源。");
                status = result.message;
            }
            catch (Exception exception)
            {
                foreach (var plan in applied.AsEnumerable().Reverse())
                    try
                    {
                        if (Hash(File.ReadAllBytes(plan.Path)) != Hash(plan.After)) continue;
                        WriteAtomic(plan.Path, plan.Before);
                        foreach (var change in result.files) if (change.path == plan.Path) change.rolledBack = true;
                    }
                    catch (Exception rollbackError) { Debug.LogWarning("[抖音自动兼容] 回退未完成，请查报告：" + rollbackError.Message); }
                result.message = exception.Message; status = "兼容停止：" + exception.Message; throw;
            }
            finally
            {
                try
                {
                    string report = Path.Combine(run, "result.json");
                    File.WriteAllText(report, JsonUtility.ToJson(result, true), Utf8);
                    File.WriteAllText(Path.Combine(ArtifactRoot, "latest-result.txt"), report, Utf8);
                    EditorPrefs.SetString(PreferencePrefix + "report", report);
                    if (result.success) Debug.Log("[抖音自动兼容] " + status + "；报告：" + report);
                }
                finally { IsBusy = false; }
            }
            return result;
        }
        private static string InsideProject(string path)
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("只处理当前工程内明确的输出，路径越界：" + full);
            for (string p = full; !string.IsNullOrEmpty(p) && p.Length > Root.Length; p = Path.GetDirectoryName(p))
                if ((Directory.Exists(p) || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("输出路径包含链接或重解析点，未写入：" + p);
            return full;
        }
        private static string Hash(byte[] bytes)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static void WriteAtomic(string file, byte[] bytes)
        {
            InsideProject(file);
            string temp = file + ".douyincompat-" + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temp, bytes); File.Replace(temp, file, null); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static void ExportPluginPackage()
        {
            if (IsBusy || BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("请等待当前处理结束。");
            var paths = new[] { PluginFolder, "Assets/Plugins/WebGL/DouyinUnity6FileAccess.jslib", "Assets/Plugins/WebGL/DouyinUnity6ImplicitExit.jslib" };
            foreach (string path in paths) if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("导出插件缺少：" + path);
            string directory = Path.Combine(ArtifactRoot, "Exports"); Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "DouyinUnity2022To6000_46f1_AutoCompat.unitypackage");
            AssetDatabase.ExportPackage(paths, file, ExportPackageOptions.Recurse);
            status = "插件已导出：" + file + "；可在2022维护；不含SDK依赖、游戏资源或存档。";
            Debug.Log("[抖音自动兼容] " + status);
        }
        [Serializable] private sealed class Command { public string id = null, operation = null, createdUtc = null; }
        [Serializable] private sealed class CommandResult { public string id, message, project, unity, report; public int processId; public bool success, compilationFailed, playing; }
        private static void ReadCommand()
        {
            if (IsBusy || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer) return;
            string directory = Path.Combine(ArtifactRoot, "Editor"), path = Path.Combine(directory, "request.json");
            if (!File.Exists(path)) return;
            Command command; try { command = JsonUtility.FromJson<Command>(File.ReadAllText(path)); } catch { return; }
            if (command == null || !Guid.TryParseExact(command.id, "N", out _)) return;
            File.Delete(path);
            var response = new CommandResult { id = command.id, project = Root, unity = Application.unityVersion,
                processId = System.Diagnostics.Process.GetCurrentProcess().Id, compilationFailed = EditorUtility.scriptCompilationFailed, playing = EditorApplication.isPlayingOrWillChangePlaymode };
            try
            {
                if (!DateTime.TryParse(command.createdUtc, null, DateTimeStyles.RoundtripKind, out DateTime time) || Math.Abs((DateTime.UtcNow - time.ToUniversalTime()).TotalMinutes) > 10) throw new InvalidOperationException("CLI请求过期。");
                if (command.operation == "run") RunCurrentExport();
                else if (command.operation == "export") ExportPluginPackage();
                else if (command.operation == "probe") DouyinCompatVerification.Run();
                else if (command.operation != "status") throw new InvalidOperationException("未知操作。");
                response.success = !response.compilationFailed; response.message = Status; response.report = LatestReportPath;
            }
            catch (Exception exception) { response.message = exception.ToString(); }
            File.WriteAllText(Path.Combine(directory, command.id + ".response.json"), JsonUtility.ToJson(response, true), Utf8);
        }
    }
    public sealed class DouyinCompatBuildCallbacks : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => int.MaxValue - 10;
        public void OnPreprocessBuild(BuildReport report)
        { if (report.summary.platform == BuildTarget.WebGL && DouyinCompatService.AutomaticEnabled && DouyinCompatService.IsTargetEditor) DouyinCompatService.ValidateBeforeBuild(); }
        public void OnPostprocessBuild(BuildReport report)
        { if (report.summary.platform == BuildTarget.WebGL && DouyinCompatService.AutomaticEnabled && DouyinCompatService.IsTargetEditor) DouyinCompatService.MarkWebGLBuildCompleted(); }
    }
}
#endif