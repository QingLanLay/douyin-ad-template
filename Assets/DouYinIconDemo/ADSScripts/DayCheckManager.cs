using UnityEngine;
using System;

/// <summary>
/// 日期检测工具 - 判断是否为新的一天，并持久化当前日期。
/// 使用 TTSaveManager（底层为 TTSDK.PlayerPrefs）存储上次记录的日期字符串。
/// </summary>
public static class DayCheckManager
{
    private const string LastSaveDateKey = "LastRecordedDate";

    /// <summary>
    /// 获取当前日期字符串（yyyy-MM-dd 格式，去除时间部分）
    /// </summary>
    public static string GetCurrentDateString()
    {
        return DateTime.Now.Date.ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// 立即保存当前日期
    /// </summary>
    public static void SaveCurrentDate()
    {
        string currentDate = GetCurrentDateString();
        TTSaveManager.Instance.SetString(LastSaveDateKey, currentDate);
        TTSaveManager.Instance.Save();
    }

    /// <summary>
    /// 检查今天是否为新的一天（与上次保存的日期不同）。
    /// 首次运行（无保存记录）视为新的一天。
    /// </summary>
    /// <returns>是新的一天返回 true，同一天返回 false</returns>
    public static bool IsNewDay()
    {
        string lastSavedDate = TTSaveManager.Instance.GetString(LastSaveDateKey, "");

        if (string.IsNullOrEmpty(lastSavedDate))
            return true;

        string currentDate = GetCurrentDateString();
        return !string.Equals(lastSavedDate, currentDate);
    }

    /// <summary>
    /// 标记今天已记录（SaveCurrentDate 的便捷包装）
    /// </summary>
    public static void MarkTodayRecorded()
    {
        SaveCurrentDate();
    }

    /// <summary>
    /// 清除保存的日期（用于测试/重置场景）
    /// </summary>
    public static void ClearSavedDate()
    {
        TTSaveManager.Instance.DeleteKey(LastSaveDateKey);
        TTSaveManager.Instance.Save();
        Debug.Log("[DayCheckManager] 已清除保存日期，下次 IsNewDay() 将返回 true");
    }
}
