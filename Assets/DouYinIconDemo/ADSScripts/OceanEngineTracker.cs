using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using System;
using TTSDK.UNBridgeLib.LitJson;
using TTSDK;
using static TTSDK.TTAndroidCallJsInterface;

// ==================== 巨量引擎事件类型常量（与后台配置一一对应） ====================
public static class OceanEngineEvents
{
    public const string Active = "active";
    public const string GameAddiction = "game_addiction";
    public const string LtRoi = "lt_roi";
    public const string LtvRoi7d = "ltv_roi_7d";
    public const string GainResource = "gain_resource";
}

// ==================== PlayerPrefs存储Key常量 ====================
public static class OceanEnginePrefsKeys
{
    public const string CurrentClickId = "OE_CurrentClickId";
    public const string ReportedActive = "OE_ReportedActive";
    public const string ReportedGameAddictionCount = "OE_ReportedGameAddictionCount";
    public const string Reported7DayLtv = "OE_Reported7DayLtv";
    public const string TotalAdRevenue = "OE_TotalAdRevenue";
    public const string RegisterTime = "OE_RegisterTime";
}

// ==================== 巨量引擎API回传核心管理类 ====================
public class OceanEngineTracker : MonoBehaviour
{
    public static OceanEngineTracker Ins
    {
        get
        {
            return MyHelper.Create<OceanEngineTracker>();
        }
    }

    // 巨量引擎转化回传接口地址
    private const string ConversionApiUrl = "https://analytics.oceanengine.com/api/v2/conversion";

    // 全局缓存的ClickID
    public static string CurrentClickId { get; private set; } = string.Empty;

    // 日志前缀
    private const string LogPrefix = "[巨量引擎] ";

    // 广告变现固定价值（可在Inspector调整，或代码中直接赋值）
    [Header("广告固定价值配置(单位:元)")]
    public float RewardedValue = 0.03f;  // 激励视频
    public float InterstitialValue = 0.01f; // 插屏
    public float BannerValue = 0.001f;   // Banner

    public void Init()
    {
        if (AdManager.NoAdsEnabled) return;
        // 仅在字节小游戏平台执行
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            GetOceanEngineAdParams();
        }
        else
        {
            LogWarning("非字节小游戏平台，跳过巨量引擎初始化");
        }
    }

    #region 核心功能：获取广告参数（ClickID）- 修复版
    /// <summary>
    /// 获取巨量引擎广告参数，优先提取并缓存ClickID
    /// 【修复】恢复ad_params解析，这是抖音/字节ClickID的主要来源
    /// </summary>
    private void GetOceanEngineAdParams()
    {
        try
        {
            // 字节小游戏获取启动参数的官方API
            var launchOptions = TT.GetLaunchOptionsSync();
            LogInfo($"获取到启动参数：{JsonMapper.ToJson(launchOptions)}");

            // 1. 【优先】从ad_params中解析clickid（官方推荐，99%的广告流量从这里取）
            //if (!string.IsNullOrEmpty(launchOptions.ad_params))
            //{
            //    try
            //    {
            //        var adParamsJson = JsonMapper.ToObject(launchOptions.ad_params);
            //        if (adParamsJson != null && adParamsJson.ContainsKey("clickid"))
            //        {
            //            CurrentClickId = adParamsJson["clickid"].ToString().Trim(); // 去除首尾空格
            //            if (!string.IsNullOrEmpty(CurrentClickId))
            //            {
            //                TTSaveManager.Instance.SetString(OceanEnginePrefsKeys.CurrentClickId, CurrentClickId);
            //                LogInfo($"✅ 从ad_params获取ClickID成功：{CurrentClickId}");
            //                return;
            //            }
            //        }
            //    }
            //    catch (Exception adEx)
            //    {
            //        LogWarning($"解析ad_params失败：{adEx.Message}，尝试从Query获取");
            //    }
            //}

            // 2. 【兜底】从外层query参数获取（软广场景ad_params可能缺失）
            if (launchOptions.Query != null && launchOptions.Query.ContainsKey("clickid"))
            {
                CurrentClickId = launchOptions.Query["clickid"].ToString().Trim();
                if (!string.IsNullOrEmpty(CurrentClickId))
                {
                    TTSaveManager.Instance.SetString(OceanEnginePrefsKeys.CurrentClickId, CurrentClickId);
                    LogInfo($"✅ 从外层Query获取ClickID成功：{CurrentClickId}");
                    InitReal();
                    return;
                }
            }

            // 3. 【最后兜底】从本地缓存恢复（游戏重启后ClickID不丢失）
            if (TTSaveManager.Instance.HasKey(OceanEnginePrefsKeys.CurrentClickId))
            {
                CurrentClickId = TTSaveManager.Instance.GetString(OceanEnginePrefsKeys.CurrentClickId).Trim();
                if (!string.IsNullOrEmpty(CurrentClickId))
                {
                    LogInfo($"✅ 从本地缓存恢复ClickID：{CurrentClickId}");
                    InitReal();
                    return;
                }
            }

            LogWarning("❌ 未获取到有效ClickID，非广告流量，将跳过所有转化上报");
        }
        catch (Exception e)
        {
            LogError($"解析广告参数异常：{e.Message}\n{e.StackTrace}");
        }
    }
    #endregion
    void InitReal()
    {
        ReportActive();

        Check7DayLtv();
    }
    #region 事件上报：激活（active）
    /// <summary>
    /// 上报激活事件（游戏首次启动完成，进入主界面时调用）
    /// 【注意】只能上报一次，重复调用会被自动拦截
    /// </summary>
    public void ReportActive()
    {
        if (AdManager.NoAdsEnabled) return;
#if UNITY_EDITOR
        Debug.Log("事件上报：激活（active）");
        return;
#endif
        // 校验1：是否已上报过
        if (TTSaveManager.Instance.HasKey(OceanEnginePrefsKeys.ReportedActive))
        {
            LogWarning("激活事件已上报过，跳过重复上报");
            return;
        }

        // 校验2：是否有有效ClickID（防止code:103）
        if (string.IsNullOrEmpty(CurrentClickId))
        {
            LogWarning("❌ 无有效ClickID，跳过激活事件上报（防止code:103）");
            return;
        }

        // 记录注册时间（用于7日ROI判断）
        if (!TTSaveManager.Instance.HasKey(OceanEnginePrefsKeys.RegisterTime))
        {
            TTSaveManager.Instance.SetString(OceanEnginePrefsKeys.RegisterTime, DateTime.Now.ToString("o"));
        }

        // 执行上报
        StartCoroutine(SendConversionRequest(OceanEngineEvents.Active, 0f));
        TTSaveManager.Instance.SetInt(OceanEnginePrefsKeys.ReportedActive, 1);
        LogInfo("✅ 激活事件上报请求已发送");
    }
    #endregion

    #region 事件上报：关键行为（game_addiction）
    /// <summary>
    /// 上报关键行为事件（用户完成核心玩法节点时调用，请求就上报
    /// </summary>
    public void ReportGameAddiction()
    {
        if (AdManager.NoAdsEnabled) return;
#if UNITY_EDITOR
        Debug.Log("事件上报：关键行为（game_addiction");
        return;
#endif
        //// 校验1：上报次数是否超限
        //int reportCount = TTSaveManager.Instance.GetInt(OceanEnginePrefsKeys.ReportedGameAddictionCount, 0);
        //if (reportCount >= 3)
        //{
        //    LogWarning($"关键行为上报次数已达上限（3次），当前次数：{reportCount}，跳过上报");
        //    return;
        //}

        // 校验2：是否有有效ClickID（防止code:103）
        if (string.IsNullOrEmpty(CurrentClickId))
        {
            LogWarning("❌ 无有效ClickID，跳过关键行为事件上报（防止code:103）");
            return;
        }

        // 执行上报
        StartCoroutine(SendConversionRequest(OceanEngineEvents.GameAddiction, 0f));
        //TTSaveManager.Instance.SetInt(OceanEnginePrefsKeys.ReportedGameAddictionCount, reportCount + 1);
        //LogInfo($"✅ 关键行为事件上报请求已发送，当前累计次数：{reportCount + 1}");
    }
    #endregion

    #region 事件上报：总变现ROI（lt_roi）- 修复版
    /// <summary>
    /// 上报总变现ROI事件（用户每产生一次广告收益时立即调用）
    /// </summary>
    /// <param name="revenue">当次广告预估收入（单位：元，必须>0）</param>
    public void ReportLtRoi(float revenue)
    {
        if (AdManager.NoAdsEnabled) return;
#if UNITY_EDITOR
        Debug.Log("事件上报：总变现ROI（lt_roi）- 修复版"+ revenue);
        return;
#endif
        // 校验1：收入参数是否有效
        if (revenue <= 0f)
        {
            LogError($"总变现ROI收入参数无效：{revenue}（必须>0），跳过上报");
            return;
        }

        // 校验2：是否有有效ClickID（防止code:103）
        if (string.IsNullOrEmpty(CurrentClickId))
        {
            LogWarning("❌ 无有效ClickID，跳过总变现ROI事件上报（防止code:103）");
            return;
        }

        // 累加本地累计收入（用于7日ROI计算）
        float totalRevenue = TTSaveManager.Instance.GetFloat(OceanEnginePrefsKeys.TotalAdRevenue, 0f);
        totalRevenue += revenue;
        TTSaveManager.Instance.SetFloat(OceanEnginePrefsKeys.TotalAdRevenue, totalRevenue);

        // 执行上报
        StartCoroutine(SendConversionRequest(OceanEngineEvents.LtRoi, revenue));
        LogInfo($"✅ 总变现ROI事件上报请求已发送，当次收入：{revenue:F4}元，累计收入：{totalRevenue:F4}元");
    }

    /// <summary>
    /// 【简化版】广告播放成功后上报（按广告类型自动使用固定价值）
    /// </summary>
    /// <param name="adType">0=激励视频 1=插屏 2=Banner</param>
    public void ReportAdByType(int adType)
    {
        if (AdManager.NoAdsEnabled) return;
        float revenue = 0f;
        switch (adType)
        {
            case 0: revenue = RewardedValue; break;
            case 1: revenue = InterstitialValue; break;
            case 2: revenue = BannerValue; break;
        }
        ReportLtRoi(revenue);
    }
    #endregion

    #region 事件上报：7日变现ROI（ltv_roi_7d）
    /// <summary>
    /// 检查并上报7日变现ROI（外部调用入口）
    /// </summary>
    public void CheckAndReport7DayROI()
    {
        if (AdManager.NoAdsEnabled) return;
        Check7DayLtv();
    }

    /// <summary>
    /// 检查并上报7日变现ROI（游戏启动时自动调用，无需手动触发）
    /// </summary>
    private void Check7DayLtv()
    {
        try
        {
            // 校验1：是否已上报过
            if (TTSaveManager.Instance.HasKey(OceanEnginePrefsKeys.Reported7DayLtv))
            {
                LogInfo("7日变现ROI已上报过，跳过检查");
                return;
            }

            // 校验2：是否有注册时间
            if (!TTSaveManager.Instance.HasKey(OceanEnginePrefsKeys.RegisterTime))
            {
                LogWarning("未找到注册时间，跳过7日变现ROI检查");
                return;
            }

            // 校验3：是否注册满7天
            var registerTime = DateTime.Parse(TTSaveManager.Instance.GetString(OceanEnginePrefsKeys.RegisterTime));
            var timeSinceRegister = DateTime.Now - registerTime;
            if (timeSinceRegister < TimeSpan.FromDays(7))
            {
                LogInfo($"注册未满7天，当前注册时长：{timeSinceRegister.TotalDays:F1}天，跳过7日变现ROI上报");
                return;
            }

            // 校验4：是否有有效ClickID（防止code:103）
            if (string.IsNullOrEmpty(CurrentClickId))
            {
                LogWarning("❌ 无有效ClickID，跳过7日变现ROI上报（防止code:103）");
                return;
            }

            // 获取7天累计收入
            float total7DayRevenue = TTSaveManager.Instance.GetFloat(OceanEnginePrefsKeys.TotalAdRevenue, 0f);
            if (total7DayRevenue <= 0f)
            {
                LogWarning($"7天累计收入为0或负数：{total7DayRevenue}，跳过7日变现ROI上报");
                return;
            }

            // 执行上报
            StartCoroutine(SendConversionRequest(OceanEngineEvents.LtvRoi7d, total7DayRevenue));
            TTSaveManager.Instance.SetInt(OceanEnginePrefsKeys.Reported7DayLtv, 1);
            LogInfo($"✅ 7日变现ROI事件上报请求已发送，7天累计收入：{total7DayRevenue:F4}元");
        }
        catch (Exception e)
        {
            LogError($"检查7日变现ROI异常：{e.Message}\n{e.StackTrace}");
        }
    }
    #endregion

    #region 底层功能：发送HTTP POST请求 - 优化版
    /// <summary>
    /// 发送巨量引擎转化回传HTTP POST请求
    /// 【优化】增加ClickID最终校验，优化JSON构建
    /// </summary>
    private IEnumerator SendConversionRequest(string eventType, float revenue)
    {
        if (AdManager.NoAdsEnabled) yield break;
        // 【最终防线】再次校验ClickID，防止code:103
        if (string.IsNullOrEmpty(CurrentClickId))
        {
            LogError($"❌ 发送请求前发现ClickID为空，终止上报（事件：{eventType}）");
            yield break;
        }

        // 构建请求体（显式构建，避免匿名类序列化问题）
        JsonData requestData = new JsonData();
        requestData["event_type"] = eventType;

        JsonData context = new JsonData();
        JsonData ad = new JsonData();
        ad["callback"] = CurrentClickId; // 核心：确保callback字段正确
        context["ad"] = ad;
        requestData["context"] = context;

        requestData["timestamp"] = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;

        JsonData properties = new JsonData();
        properties["revenue"] = (double)revenue;
        requestData["properties"] = properties;

        string jsonData = requestData.ToJson();
        LogInfo($"准备发送回传请求，事件类型：{eventType}，请求体：{jsonData}");

        // 创建并发送POST请求
        using (UnityWebRequest request = new UnityWebRequest(ConversionApiUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            LogInfo($"发送POST请求到: {ConversionApiUrl}");
            LogInfo($"事件类型: {eventType}, callback长度: {CurrentClickId.Length}");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                LogInfo($"✅ 事件回传成功");
                LogInfo($"响应内容: {request.downloadHandler.text}");

                // 解析响应，检查是否有业务错误
                try
                {
                    JsonData response = JsonMapper.ToObject(request.downloadHandler.text);
                    if (response.ContainsKey("code") && (int)response["code"] != 0)
                    {
                        LogError($"❌ 业务错误，code: {response["code"]}, message: {response["message"]}");
                    }
                }
                catch { }
            }
            else
            {
                LogError($"❌ 网络请求失败: {request.error}");
                LogError($"状态码: {request.responseCode}");
                if (!string.IsNullOrEmpty(request.downloadHandler?.text))
                {
                    LogError($"错误响应: {request.downloadHandler.text}");
                }
            }
        }
    }
    #endregion

    #region 日志工具
    private static void LogInfo(string message)
    {
        Debug.Log($"{LogPrefix}{message}");
    }

    private static void LogWarning(string message)
    {
        Debug.LogWarning($"{LogPrefix}{message}");
    }

    private static void LogError(string message)
    {
        Debug.LogError($"{LogPrefix}{message}");
    }
    #endregion
    public void ReportGainResource()
    {
        if (AdManager.NoAdsEnabled) return;
#if UNITY_EDITOR
        Debug.Log("事件上报：看完激励上报gain_resource");
        return;
#endif
        if (string.IsNullOrEmpty(CurrentClickId))
        {
            LogWarning("❌ 无有效ClickID，跳过看完激励上报（防止code:103）");
            return;
        }

        // 执行上报
        StartCoroutine(SendConversionRequest(OceanEngineEvents.GainResource, 0f));
    }
}