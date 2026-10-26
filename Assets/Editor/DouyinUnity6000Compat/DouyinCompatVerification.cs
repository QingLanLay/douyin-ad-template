#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace DouyinUnity6000Compat
{
    /// <summary>显式执行的纯结构/稳定门控探针，不读取或修改实际游戏导出。</summary>
    internal static class DouyinCompatVerification
    {
        internal static void Run()
        {
            const string fixture = "var SYSCALLS={};function exitJS(status,implicit){EXITSTATUS=status;_proc_exit(status);}";
            var first = DouyinCompatCore.Plan(fixture);
            Require(first.AccessChanged && first.ExitChanged, "两项结构补回");
            var second = DouyinCompatCore.Plan(first.Text);
            Require(!second.AccessChanged && !second.ExitChanged && second.Text == first.Text, "重复执行幂等");
            bool rejected = false;
            try { DouyinCompatCore.Plan("var other={};"); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "未知结构拒绝");
            var gate = new DouyinCompatWatchGate();
            Require(!gate.Observe("a", 0, false) && !gate.Observe("a", 1, false) && gate.Observe("a", 2, false), "稳定后触发");
            Require(!gate.Observe("a", 5, false), "不重复触发");
            Require(!gate.Observe("b", 6, true), "忙碌不触发");
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(root, ".artifacts/DouyinAutoCompat/Probe");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "result.json"), "{\"passed\":true,\"checks\":6,\"scope\":\"Editor-loaded structural core and watch gate only\",\"platformLoadVerified\":false}", new UTF8Encoding(false));
            Debug.Log("[抖音自动兼容] 6项结构/门控探针通过；不是实际SDK构建或平台验收。");
        }
        private static void Require(bool value, string reason)
        { if (!value) throw new InvalidOperationException("兼容探针失败：" + reason); }
    }
}
#endif