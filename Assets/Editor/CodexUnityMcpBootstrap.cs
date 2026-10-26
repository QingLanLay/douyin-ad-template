using System;
using UnityEditor;
using UnityEngine;

/// <summary>本工程的 Codex MCP 后台桥，不修改游戏资源和构建设置。</summary>
[InitializeOnLoad]
public static class CodexUnityMcpBootstrap
{
    static CodexUnityMcpBootstrap()
    {
        if (Application.isBatchMode) return;
        // 本工程已由安装器配置，避免插件在 delayCall 中重写全局客户端配置。
        SessionState.SetBool("MCPForUnity.StartupConfigRewrite.Ran", true);
        EditorApplication.delayCall += StartBridge;
    }

    [MenuItem("Tools/Codex/Start Unity MCP Bridge")]
    public static void StartBridge()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += StartBridge;
            return;
        }
        Type bridge = Type.GetType(
            "MCPForUnity.Editor.Services.Transport.Transports.StdioBridgeHost, MCPForUnity.Editor");
        if (bridge == null) return;
        try
        {
            if (!(bool)bridge.GetProperty("IsRunning").GetValue(null))
                bridge.GetMethod("StartAutoConnect").Invoke(null, null);
            Debug.Log("[Codex Unity MCP] 后台桥已启动：" + Application.dataPath);
        }
        catch (Exception exception)
        {
            Debug.LogError("[Codex Unity MCP] 启动失败：" + exception.GetBaseException().Message);
        }
    }
}
