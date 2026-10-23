using System;
using System.Collections.Generic;

/// <summary>直玩细分生命周期；记录信号到达与可实际展示是两个不同阶段。</summary>
public enum FeedLifecycleStage
{
    Idle,
    LaunchStarted,
    ResolvingConfiguration,
    SchemePrepared,
    ResourcesReady,
    SceneReady,
    CardShowRequested,
    CardShown,
    CardHidden,
    FeedEnterRequested,
    FullGameEntered,
    FullGameHidden,
    FullGameShown,
    FeedExited,
    SessionEnded,
    ConfigurationFailed
}

/// <summary>同一管理器保留最近 120 条时间线，编辑器预览与正式日志使用同一状态来源。</summary>
public sealed class FeedLifecycleRecord
{
    public readonly FeedLifecycleStage Stage;
    public readonly string Detail;
    public readonly string ContentId;
    public readonly int LaunchVersion;
    public readonly DateTime Timestamp;

    public FeedLifecycleRecord(FeedLifecycleStage stage, string detail, FeedLaunchContext context, int version)
    {
        Stage = stage;
        Detail = detail;
        ContentId = context == null ? string.Empty : context.ContentId;
        LaunchVersion = version;
        Timestamp = DateTime.Now;
    }

    public override string ToString() => $"{Timestamp:HH:mm:ss.fff}  #{LaunchVersion} {Stage}  {Detail}";
}

/// <summary>下一 ID 的确定性选择规则：跳过空项/无效枚举，可限制同渠道并选择是否循环。</summary>
public static class FeedPreviewSequence
{
    public static bool IsValid(FeedContentConfig item)
    {
        return item != null && !string.IsNullOrWhiteSpace(item.contentId) &&
               (item.channel == FeedUserChannel.Acquisition || item.channel == FeedUserChannel.ReturnVisit) &&
               Enum.IsDefined(typeof(FeedExperienceScheme), item.scheme) &&
               (item.channel != FeedUserChannel.ReturnVisit || (item.returnSceneId >= 1 && item.returnSceneId <= 3));
    }

    public static int FindNextIndex(IReadOnlyList<FeedContentConfig> items, int current,
        bool sameChannel = true, bool loop = true)
    {
        if (items == null || items.Count == 0) return -1;
        bool hasCurrent = current >= 0 && current < items.Count && IsValid(items[current]);
        FeedUserChannel channel = hasCurrent ? items[current].channel : FeedUserChannel.Unknown;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (hasCurrent) seen.Add(((int)channel) + ":" + items[current].contentId.Trim());
        int start = current >= 0 && current < items.Count ? current : -1;
        for (int offset = 1; offset <= items.Count; offset++)
        {
            int candidate = start + offset;
            if (candidate >= items.Count)
            {
                if (!loop) break;
                candidate %= items.Count;
            }
            FeedContentConfig item = items[candidate];
            if (!IsValid(item) || (sameChannel && channel != FeedUserChannel.Unknown && item.channel != channel)) continue;
            if (!seen.Add(((int)item.channel) + ":" + item.contentId.Trim())) continue;
            return candidate;
        }
        return -1;
    }
}
