using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 推荐流用户来源渠道，与平台启动参数 feed_game_channel 对应。
/// </summary>
public enum FeedUserChannel
{
    /// <summary>未知或无法识别的渠道。</summary>
    Unknown = 0,

    /// <summary>复访用户渠道。</summary>
    ReturnVisit = 1,

    /// <summary>获客用户渠道。</summary>
    Acquisition = 2
}

/// <summary>
/// 推荐流进入游戏后可展示的体验方案。
/// </summary>
public enum FeedExperienceScheme
{
    /// <summary>先播放推荐视频。</summary>
    Video = 1,

    /// <summary>直接进入指定试玩关卡。</summary>
    TrialLevel = 2,

    /// <summary>直接进入指定新手引导。</summary>
    NewPlayerGuide = 3
}

/// <summary>
/// 单条推荐流内容配置，可由本地配置或远程配置生成。
/// </summary>
[Serializable]
public sealed class FeedContentConfig
{
    /// <summary>平台下发的推荐内容唯一标识。</summary>
    public string contentId;

    /// <summary>该内容适用的用户来源渠道。</summary>
    public FeedUserChannel channel = FeedUserChannel.Acquisition;

    /// <summary>复访业务场景：1=离线收益、2=体力恢复、3=重要事件提醒；获客填 0。</summary>
    [Range(0, 3)] public int returnSceneId;

    /// <summary>命中配置后采用的体验方案。</summary>
    public FeedExperienceScheme scheme = FeedExperienceScheme.Video;

    /// <summary>由项目自行解释的业务参数，如视频地址或关卡 ID。</summary>
    public string payload;

    /// <summary>仅供策划或开发人员阅读的配置说明。</summary>
    [TextArea(2, 4)] public string description;
}

/// <summary>
/// 远程 JSON 中的紧凑配置项；短字段名用于减小配置体积。
/// </summary>
[Serializable]
public sealed class FeedRemoteConfigItem
{
    /// <summary>内容 ID，对应本地配置的 contentId。</summary>
    public string id;

    /// <summary>用户渠道枚举值。</summary>
    public int c;

    /// <summary>复访业务场景编号；获客为 0。</summary>
    public int r;

    /// <summary>体验方案枚举值。</summary>
    public int s;

    /// <summary>业务参数，对应本地配置的 payload。</summary>
    public string p;
}

/// <summary>
/// 远程配置文件的 JSON 根对象。
/// </summary>
[Serializable]
public sealed class FeedRemoteConfig
{
    public List<FeedRemoteConfigItem> items = new List<FeedRemoteConfigItem>();

    /// <summary>
    /// 按用户渠道和内容 ID 查找配置，并转换为统一的内容配置对象。
    /// </summary>
    public FeedContentConfig Find(FeedUserChannel channel, string contentId)
    {
        if (items == null || string.IsNullOrWhiteSpace(contentId)) return null;

        FeedRemoteConfigItem item = items.Find(value =>
            value != null && value.c == (int)channel &&
            string.Equals(value.id, contentId, StringComparison.Ordinal));

        // 对远程枚举值进行校验，避免无效配置流入项目业务层。
        if (item == null ||
            !Enum.IsDefined(typeof(FeedExperienceScheme), item.s) ||
            (channel == FeedUserChannel.ReturnVisit && (item.r < 1 || item.r > 3))) return null;

        return new FeedContentConfig
        {
            contentId = item.id,
            channel = channel,
            returnSceneId = item.r,
            scheme = (FeedExperienceScheme)item.s,
            payload = item.p,
            description = "远程配置"
        };
    }
}

/// <summary>
/// 推荐流配置资产，保存远程地址及远程请求失败时使用的本地兜底配置。
/// </summary>
[CreateAssetMenu(fileName = "FeedRecommendationConfig", menuName = "抖音/推荐流配置", order = 1)]
public sealed class FeedRecommendationConfig : ScriptableObject
{
    [Header("远程配置")]
    // 服务器 JSON 配置地址；留空时直接使用本地配置。
    [SerializeField] private string remoteConfigUrl;
    // 远程请求超时时间，限制在 2～30 秒之间。
    [Range(2, 30)] [SerializeField] private int remoteTimeoutSeconds = 6;

    [Header("本地兜底配置")]
    // 远程请求失败或未命中内容 ID 时使用的备用配置。
    [SerializeField] private List<FeedContentConfig> contents = new List<FeedContentConfig>();

    public string RemoteConfigUrl => remoteConfigUrl == null ? string.Empty : remoteConfigUrl.Trim();
    public int RemoteTimeoutSeconds => Mathf.Clamp(remoteTimeoutSeconds, 2, 30);
    public IReadOnlyList<FeedContentConfig> Contents => contents;

    /// <summary>
    /// 按用户渠道和内容 ID 查找本地兜底配置。
    /// </summary>
    public FeedContentConfig Find(FeedUserChannel channel, string contentId)
    {
        if (channel == FeedUserChannel.Unknown || string.IsNullOrWhiteSpace(contentId)) return null;
        return contents.Find(item => item != null && item.channel == channel &&
            string.Equals(item.contentId, contentId, StringComparison.Ordinal));
    }

    /// <summary>返回指定渠道的首条有效配置，供未知 ContentID 安全兜底。</summary>
    public FeedContentConfig FindRoute(FeedUserChannel channel, string route)
    {
        return contents.Find(item => item != null && item.channel == channel &&
            string.Equals(item.payload, route, StringComparison.Ordinal));
    }

    public FeedContentConfig FindFirst(FeedUserChannel channel)
    {
        if (channel == FeedUserChannel.Unknown) return null;
        return contents.Find(item => item != null && item.channel == channel &&
            !string.IsNullOrWhiteSpace(item.contentId));
    }

    /// <summary>
    /// 将当前本地配置转换为可部署到服务器的紧凑 JSON。
    /// </summary>
    /// <param name="prettyPrint">是否输出带缩进和换行的格式化 JSON。</param>
    public string ToRemoteJson(bool prettyPrint = false)
    {
        var data = new FeedRemoteConfig();
        foreach (FeedContentConfig content in contents)
        {
            if (content == null || string.IsNullOrWhiteSpace(content.contentId)) continue;
            data.items.Add(new FeedRemoteConfigItem
            {
                id = content.contentId.Trim(),
                c = (int)content.channel,
                r = content.returnSceneId,
                s = (int)content.scheme,
                p = content.payload == null ? string.Empty : content.payload.Trim()
            });
        }
        return JsonUtility.ToJson(data, prettyPrint);
    }
}
