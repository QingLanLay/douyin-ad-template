using System;
using UnityEngine;


/// <summary>
/// 体力系统
/// 使用时间戳进行恢复计时，支持离线恢复
/// </summary>
public class EnergySystem : MonoBehaviour
{
    /// <summary>
    /// 单例实例
    /// </summary>
    public static EnergySystem Instance { get; private set; }

    [Header("配置")]
    [Tooltip("体力上限")]
    public int maxEnergy = 10;

    [Tooltip("体力恢复间隔（秒）")]
    public int recoverySeconds = 300;

    [Tooltip("无限体力最短持续时间（分钟）")]
    public int infiniteMinMinutes = 30;

    [Tooltip("无限体力最长持续时间（分钟）")]
    public int infiniteMaxMinutes = 60;

    [Tooltip("广告奖励体力数量")]
    public int adRewardEnergy = 5;

    [Header("无限体力固定时长")]
    [Tooltip("固定时长（分钟），0=使用随机范围。Inspector中设为大于0的值即可固定")]
    public int fixedInfiniteMinutes = 0;

    // Storage Keys
    private const string KEY_ENERGY = "e_cur";
    private const string KEY_LAST_ONLINE = "e_online";
    private const string KEY_INFINITE_END = "e_inf";
    private const string KEY_WATCH_COUNT = "e_wc";
    private const string KEY_WATCH_START = "e_ws";

    /// <summary>
    /// 体力变化时触发
    /// </summary>
    public event Action OnChanged;

    /// <summary>
    /// 离线恢复时触发，返回恢复的体力数量
    /// </summary>
    public event Action<int> OnOfflineRecovered;

    // 当前体力
    private int _energy;
    // 无限体力结束时间戳（毫秒）
    private long _infiniteEndTime;
    // 广告观看次数
    private int _watchCount;
    // 广告观看开始时间戳（毫秒）
    private long _watchStartTime;

    /// <summary>
    /// Unity生命周期 - 初始化（在Start之前执行）
    /// 功能：单例模式实现，确保只有一个EnergySystem实例存在
    /// </summary>
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // 设置为 DontDestroyOnLoad，使对象在场景切换时不被销毁，保持体力数据不丢失
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            // 如果已存在实例，销毁重复创建的对象
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Unity生命周期 - 首次Update之前执行一次
    /// 功能：初始化体力系统数据（加载本地存储、计算离线恢复、触发初始UI更新）
    /// </summary>
    private void Start()
    {
        Init(); // 加载数据 + 计算离线恢复 + 触发OnChanged事件
    }

    /// <summary>
    /// Unity生命周期 - 每帧执行
    /// 功能：
    /// 1. 检查是否需要恢复体力（基于时间戳判断恢复间隔）
    /// 2. 检查无限体力是否过期
    /// </summary>
    private void Update()
    {
        // 仅在非无限体力且未满体力时才检查恢复
        if (!IsInfinite() && _energy < maxEnergy)
        {
            RecoverByTimestamp();
        }

        // 检查无限体力是否到期
        CheckInfiniteExpired();
    }

    /// <summary>
    /// Unity生命周期 - 对象被销毁时执行
    /// 功能：保存在线时间戳，确保下次启动时能正确计算离线恢复
    /// 触发时机：场景切换、游戏关闭、对象被手动销毁等
    /// </summary>
    private void OnDestroy()
    {
        SaveLastOnline();
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Unity生命周期 - 应用暂停/恢复时执行（如切换到后台/回到前台）
    /// 功能：
    /// - pauseStatus=true（暂停）：保存当前时间戳，用于离线恢复计算
    /// - pauseStatus=false（恢复）：立即计算并应用离线期间应恢复的体力
    /// 典型场景：玩家按Home键切到手机桌面再回来
    /// </summary>
    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            SaveLastOnline();
        }
        else
        {
            RecoverOffline();
        }
    }

    /// <summary>
    /// Unity生命周期 - 应用退出时执行（如关闭游戏）
    /// 功能：保存最后在线时间戳，确保下次启动时能正确计算离线恢复
    /// 注意：在移动端可能不会被调用（进程被系统直接杀死），因此OnApplicationPause是更可靠的保障
    /// </summary>
    private void OnApplicationQuit()
    {
        SaveLastOnline();
    }

    #region 公开接口

    /// <summary>
    /// 获取体力显示文本
    /// </summary>
    /// <returns>体力值或无限符号 "∞"</returns>
    public string GetDisplayText() => IsInfinite() ? "∞" : Mathf.Clamp(_energy, 0, 99).ToString();

    /// <summary>
    /// 获取体力恢复倒计时文本
    /// </summary>
    /// <returns>格式为 "MM:SS"，体力满时返回空字符串</returns>
    public string GetCountdownText()
    {
        if (IsInfinite() || _energy >= maxEnergy) return "";
        long lastOnline = GetLastOnlineTime();
        long elapsed = GetTimeMs() - lastOnline;
        float remaining = recoverySeconds - (elapsed / 1000f);
        if (remaining <= 0) return "00:00";
        return $"{(int)(remaining / 60):D2}:{(int)(remaining % 60):D2}";
    }

    /// <summary>获取体力恢复剩余秒数</summary>
    /// <returns>剩余秒数，已满返回0</returns>
    public int GetCountdownSeconds()
    {
        if (IsInfinite() || _energy >= maxEnergy) return 0;
        long lastOnline = GetLastOnlineTime();
        long elapsed = GetTimeMs() - lastOnline;
        int remaining = recoverySeconds - (int)(elapsed / 1000);
        return Mathf.Max(remaining, 0);
    }

    /// <summary>
    /// 获取无限体力剩余时间文本
    /// </summary>
    /// <returns>格式为 "HH:MM:SS"，无限体力到期时返回空字符串</returns>
    public string GetInfiniteTimeText()
    {
        if (!IsInfinite()) return "";
        long remaining = _infiniteEndTime - GetTimeMs();
        if (remaining <= 0) return "";
        int totalSeconds = (int)(remaining / 1000);
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int seconds = totalSeconds % 60;
        return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
    }

    /// <summary>
    /// 获取离线恢复预计时间
    /// </summary>
    /// <returns>预计恢复满体力还需的时间，"已满" 或 "HH:MM:SS"</returns>
    public string GetOfflineRecoverTimeText()
    {
        if (IsInfinite() || _energy >= maxEnergy) return "已满";
        long lastOnline = GetLastOnlineTime();
        long elapsed = GetTimeMs() - lastOnline;
        int remaining = maxEnergy - _energy;
        long neededMs = (long)remaining * recoverySeconds * 1000L;
        long totalMs = lastOnline + neededMs - GetTimeMs();
        if (totalMs <= 0) return "已满";
        int totalSeconds = (int)(totalMs / 1000);
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int seconds = totalSeconds % 60;
        return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
    }

    /// <summary>
    /// 检查是否有可用体力
    /// </summary>
    /// <returns>有体力返回true，无体力返回false</returns>
    public bool HasEnergy() => IsInfinite() || _energy > 0;

    /// <summary>
    /// 检查当前是否为无限体力状态
    /// </summary>
    /// <returns>无限体力状态返回true</returns>
    public bool IsInfinite() => GetTimeMs() < _infiniteEndTime;

    /// <summary>
    /// 尝试消耗1点体力
    /// </summary>
    /// <returns>消耗成功返回true，体力不足返回false</returns>
    public bool TryConsume()
    {
        if (IsInfinite())
        {
            OnChanged?.Invoke();
            return true;
        }
        if (_energy > 0)
        {
            _energy--;
            Save();
            OnChanged?.Invoke();
            return true;
        }
        OnChanged?.Invoke();
        return false;
    }

    /// <summary>
    /// 观看广告增加体力
    /// </summary>
    /// <param name="onComplete">广告观看完成回调（体力已增加）</param>
    public void AddByAd(Action onComplete = null, Action onFailed = null)
    {
        Action grantEnergy = () =>
        {
            _energy = Mathf.Min(_energy + adRewardEnergy, maxEnergy);
            Save();
            OnChanged?.Invoke();
            onComplete?.Invoke();
        };

        // 所有构建平台均先判断 noAds，不依赖 Editor 宏、SDK 初始化或广告管理器就绪。
        if (AdManager.NoAdsEnabled)
        {
            grantEnergy();
            return;
        }
#if UNITY_EDITOR
        grantEnergy();
        Debug.Log($"[Energy] Editor模式 - 跳过广告，直接+{adRewardEnergy}体力");
#else
        if (AdManager.Instance == null)
        {
            Debug.LogWarning("[Energy] 常驻 DouyinAdSystem 上没有 AdManager，无法播放激励广告");
            onFailed?.Invoke();
            return;
        }

        AdManager.Instance.ShowRewardVideoAd(grantEnergy, onFailed);
#endif
    }

    /// <summary>
    /// 观看广告激活无限体力（需观看2次广告）
    /// </summary>
    /// <param name="onComplete">激活成功回调</param>
    public void ActivateInfiniteByAd(Action onComplete = null, Action onFailed = null)
    {
        ResetExpiredWatch();

        Action recordCompletedAd = () =>
        {
            if (_watchCount <= 0)
                _watchStartTime = GetTimeMs();

            _watchCount++;
            SaveWatch();

            if (_watchCount >= 2)
            {
                ActivateInfinite();
                ResetWatch();
            }

            OnChanged?.Invoke();
            onComplete?.Invoke();
        };

        if (AdManager.NoAdsEnabled)
        {
            recordCompletedAd.Invoke();
            return;
        }
#if UNITY_EDITOR
        recordCompletedAd.Invoke();
#else
        if (AdManager.Instance == null)
        {
            Debug.LogWarning("[Energy] 常驻 DouyinAdSystem 上没有 AdManager，无法播放无限体力激励广告");
            onFailed?.Invoke();
            return;
        }

        AdManager.Instance.ShowRewardVideoAd(recordCompletedAd, onFailed);
#endif
    }

    /// <summary>
    /// 获取广告激活进度
    /// </summary>
    /// <returns>元组 (当前已观看次数, 目标次数)</returns>
    public (int cur, int target) GetAdProgress() => (_watchCount, 2);

    /// <summary>
    /// 重置体力（用于测试或特殊场景）
    /// </summary>
    public void Reset()
    {
        _energy = maxEnergy;
        _infiniteEndTime = 0;
        ResetWatch();
        Save();
        OnChanged?.Invoke();
    }

    /// <summary>激活固定时长的无限体力（跳过广告计数，直接生效）</summary>
    /// <param name="minutes">持续时间（分钟）。传0则用 fixedInfiniteMinutes 属性值</param>
    public void ActivateInfiniteFixed(int minutes = 0)
    {
        int dur = minutes > 0 ? minutes : fixedInfiniteMinutes;
        if (dur <= 0) dur = 30;
        _infiniteEndTime = GetTimeMs() + (dur * 60 * 1000L);
        _energy = 999;
        ResetWatch();
        Save();
        OnChanged?.Invoke();
        Debug.Log($"[Energy] 固定激活无限体力 {dur}分钟");
    }

    /// <summary>扣除体力（测试用）</summary>
    /// <param name="amount">扣除数量，不低于0</param>
    public void DeductEnergy(int amount = 1)
    {
        if (IsInfinite()) return;
        _energy = Mathf.Max(_energy - amount, 0);
        Save();
        OnChanged?.Invoke();
        Debug.Log($"[Energy] 扣除{amount}体力, 当前={_energy}");
    }

    #endregion

    #region 内部逻辑

    /// <summary>
    /// 初始化体力数据
    /// </summary>
    private void Init()
    {
        Load();
        RecoverOffline();
        OnChanged?.Invoke();
    }

    /// <summary>
    /// 从本地加载数据
    /// </summary>
    private void Load()
    {
        _energy = TTSaveManager.Instance.GetInt(KEY_ENERGY, maxEnergy);
        _infiniteEndTime = long.Parse(TTSaveManager.Instance.GetString(KEY_INFINITE_END, "0"));
        _watchCount = TTSaveManager.Instance.GetInt(KEY_WATCH_COUNT, 0);
        _watchStartTime = long.Parse(TTSaveManager.Instance.GetString(KEY_WATCH_START, "0"));
    }

    /// <summary>
    /// 保存数据到本地
    /// </summary>
    private void Save()
    {
        TTSaveManager.Instance.SetInt(KEY_ENERGY, _energy);
        TTSaveManager.Instance.SetString(KEY_INFINITE_END, _infiniteEndTime.ToString());
        TTSaveManager.Instance.Save();
    }

    /// <summary>
    /// 保存最后在线时间
    /// </summary>
    private void SaveLastOnline()
    {
        TTSaveManager.Instance.SetString(KEY_LAST_ONLINE, GetTimeMs().ToString());
        TTSaveManager.Instance.Save();
    }

    /// <summary>
    /// 获取上次在线时间戳（毫秒）
    /// </summary>
    private long GetLastOnlineTime()
    {
        return long.Parse(TTSaveManager.Instance.GetString(KEY_LAST_ONLINE, GetTimeMs().ToString()));
    }

    /// <summary>
    /// 激活无限体力：fixedInfiniteMinutes>0 则用固定时长，否则用随机范围
    /// </summary>
    private void ActivateInfinite()
    {
        int minutes = fixedInfiniteMinutes > 0
            ? fixedInfiniteMinutes
            : UnityEngine.Random.Range(infiniteMinMinutes, infiniteMaxMinutes + 1);
        _infiniteEndTime = GetTimeMs() + (minutes * 60 * 1000L);
        _energy = 999; // 标记为无限体力
        Save();
        Debug.Log($"[Energy] 激活无限体力 {minutes}分钟 ({(fixedInfiniteMinutes > 0 ? "固定" : "随机")})");
    }

    /// <summary>
    /// 检查无限体力是否已过期，过期则恢复为正常体力
    /// </summary>
    private void CheckInfiniteExpired()
    {
        if (_infiniteEndTime > 0 && GetTimeMs() >= _infiniteEndTime)
        {
            _energy = maxEnergy;
            _infiniteEndTime = 0;
            ResetWatch();
            Save();
            Debug.Log("[Energy] 无限体力过期");
            OnChanged?.Invoke();
        }
    }

    /// <summary>
    /// 离线恢复体力（基于时间戳计算）
    /// </summary>
    private void RecoverOffline()
    {
        if (IsInfinite() || _energy >= maxEnergy) return;

        long lastOnline = GetLastOnlineTime();
        long currentTime = GetTimeMs();
        long elapsed = currentTime - lastOnline;

        // 计算离线期间应该恢复的体力
        int recovered = (int)(elapsed / 1000 / recoverySeconds);

        if (recovered > 0)
        {
            int oldEnergy = _energy;
            _energy = Mathf.Min(_energy + recovered, maxEnergy);
            int actualRecovered = _energy - oldEnergy;

            if (actualRecovered > 0)
            {
                Debug.Log($"[Energy] 离线恢复 +{actualRecovered} 体力");
                OnOfflineRecovered?.Invoke(actualRecovered);
            }

            Save();
            OnChanged?.Invoke();
        }

        // 更新最后在线时间
        SaveLastOnline();
    }

    /// <summary>
    /// 基于时间戳尝试恢复体力（每帧检查）
    /// </summary>
    private void RecoverByTimestamp()
    {
        if (IsInfinite() || _energy >= maxEnergy) return;

        long lastOnline = GetLastOnlineTime();
        long elapsed = GetTimeMs() - lastOnline;

        if (elapsed >= recoverySeconds * 1000L)
        {
            _energy++;
            SaveLastOnline();
            Save();
            OnChanged?.Invoke();
        }
    }

    /// <summary>
    /// 重置过期的广告观看记录（超过24小时）
    /// </summary>
    private void ResetExpiredWatch()
    {
        if (_watchStartTime <= 0) return;
        if (GetTimeMs() - _watchStartTime >= 24 * 60 * 60 * 1000L)
        {
            ResetWatch();
        }
    }

    /// <summary>
    /// 重置广告观看次数
    /// </summary>
    private void ResetWatch()
    {
        _watchCount = 0;
        _watchStartTime = 0;
        SaveWatch();
    }

    /// <summary>
    /// 保存广告观看数据到存档管理器
    /// </summary>
    private void SaveWatch()
    {
        TTSaveManager.Instance.SetInt(KEY_WATCH_COUNT, _watchCount);
        TTSaveManager.Instance.SetString(KEY_WATCH_START, _watchStartTime.ToString());
        TTSaveManager.Instance.Save();
    }

    /// <summary>
    /// 获取UTC时间戳（毫秒）
    /// </summary>
    private long GetTimeMs() => (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;

    #endregion
}
