using System;
using System.Collections.Generic;
using TTSDK;
using UnityEngine;

/// <summary>
/// 抖音存档管理器（基于 StarkFileSystemManager 同步文件存储）
/// 替换旧版 TTSDK.PlayerPrefs（异步 IndexedDB），使用 TTFileSystemManager.WriteFileSync/ReadFileSync 实现真正的同步磁盘写入。
/// 抖音模式存储为 gamedata.json；AdManager.noAds 模式标量走 Unity PlayerPrefs，JSON 走独立本地文件。
/// </summary>
public class TTSaveManager
{
    private static TTSaveManager _instance;
    public static TTSaveManager Instance => _instance ??= new TTSaveManager();

    private const string SAVE_FILE_NAME = "gamedata.json";
    private const string MIGRATED_FLAG = "__migrated_from_playerprefs";

    [Serializable]
    private class SaveData
    {
        public List<KVPair<int>> intValues = new();
        public List<KVPair<float>> floatValues = new();
        public List<KVPair<string>> stringValues = new();
    }

    [Serializable]
    private class KVPair<T>
    {
        public string k;
        public T v;
    }

    private Dictionary<string, int> _ints = new();
    private Dictionary<string, float> _floats = new();
    private Dictionary<string, string> _strings = new();
    private bool _dirty;
    private bool _initialized;

    #region 文件系统抽象（WebGL 走 SDK，Editor 走 System.IO）

    private string SaveFilePath
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return TTFileSystemManager.FixFilePath(SAVE_FILE_NAME);
#else
            return Application.persistentDataPath + "/" + SAVE_FILE_NAME;
#endif
        }
    }

    private bool FileExists(string path)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return TTFileSystemManagerWebGL.Instance.AccessSync(path);
#else
        return System.IO.File.Exists(path);
#endif
    }

    private string ReadFileText(string path)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return TTFileSystemManagerWebGL.Instance.ReadFileSync(path, "utf8");
#else
        try { return System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8); }
        catch { return null; }
#endif
    }

    private void WriteFileText(string path, string content)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string err = TTFileSystemManagerWebGL.Instance.WriteFileSync(path, content, "utf8");
        if (!string.IsNullOrEmpty(err))
            Debug.LogError($"[TTSaveManager] WriteFileSync 失败: {err}");
#else
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(path, content, System.Text.Encoding.UTF8);
        }
        catch (Exception e)
        {
            Debug.LogError($"[TTSaveManager] 写入文件失败: {e.Message}");
        }
#endif
    }

    private void DeleteFile(string path)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (TTFileSystemManagerWebGL.Instance.AccessSync(path))
            TTFileSystemManagerWebGL.Instance.UnlinkSync(path);
#else
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        catch (Exception e) { Debug.LogError($"[TTSaveManager] 删除文件失败: {e.Message}"); }
#endif
    }

    #endregion

    #region 初始化 & 存档迁移

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        LoadFromDisk();
    }

    private void LoadFromDisk()
    {
        string path = SaveFilePath;
        if (FileExists(path))
        {
            string json = ReadFileText(path);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    SaveData data = JsonUtility.FromJson<SaveData>(json);
                    if (data != null)
                    {
                        _ints.Clear();
                        _floats.Clear();
                        _strings.Clear();
                        if (data.intValues != null)
                            foreach (var kv in data.intValues) _ints[kv.k] = kv.v;
                        if (data.floatValues != null)
                            foreach (var kv in data.floatValues) _floats[kv.k] = kv.v;
                        if (data.stringValues != null)
                            foreach (var kv in data.stringValues) _strings[kv.k] = kv.v;
                        Debug.Log($"[TTSaveManager] 从文件加载成功 ({_ints.Count + _floats.Count + _strings.Count} 项)");
                        return;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[TTSaveManager] JSON 解析失败，尝试迁移旧存档: {e.Message}");
                }
            }
        }

        // 文件不存在或读取失败 → 尝试从旧 PlayerPrefs 迁移
        MigrateFromOldPlayerPrefs();
    }

    /// <summary> 首次启动：从旧版 TTSDK.PlayerPrefs（IndexedDB）迁移数据到文件系统 </summary>
    private void MigrateFromOldPlayerPrefs()
    {
        Debug.Log("[TTSaveManager] 开始从旧 PlayerPrefs 迁移存档...");
        int migrated = 0;

        // 迁移关卡数据（遍历 1~100）
        for (int i = 1; i <= 100; i++)
        {
            MigrateInt($"LevelUnlocked_{i}", ref migrated);
            MigrateInt($"LevelCompleted_{i}", ref migrated);
        }

        // 迁移已知的独立 key
        MigrateInt("HintImageCount", ref migrated);
        MigrateInt("AnswerHintCount", ref migrated);
        MigrateString("ExitGame", ref migrated);
        MigrateString("LastSaveDate", ref migrated);
        MigrateBool("SideBarReward", ref migrated);

        // 体力系统 key
        MigrateInt("Energy", ref migrated);
        MigrateString("Energy_InfiniteEnd", ref migrated);
        MigrateInt("Energy_WatchCount", ref migrated);
        MigrateString("Energy_WatchStart", ref migrated);
        MigrateString("Energy_LastOnline", ref migrated);

        // 迁移完成后写入文件
        _dirty = true;
        FlushToDisk();
        _strings[MIGRATED_FLAG] = "1";
        Debug.Log($"[TTSaveManager] 迁移完成，共 {migrated} 项");
    }

    private void MigrateInt(string key, ref int counter)
    {
        try
        {
            if (PlayerPrefs.HasKey(key))
            {
                _ints[key] = PlayerPrefs.GetInt(key);
                counter++;
            }
        }
        catch { /* 旧 PlayerPrefs 不可用时忽略 */ }
    }

    private void MigrateString(string key, ref int counter)
    {
        try
        {
            if (PlayerPrefs.HasKey(key))
            {
                _strings[key] = PlayerPrefs.GetString(key);
                counter++;
            }
        }
        catch { }
    }

    private void MigrateBool(string key, ref int counter)
    {
        try
        {
            if (PlayerPrefs.HasKey(key))
            {
                _ints[key] = PlayerPrefs.GetInt(key);
                counter++;
            }
        }
        catch { }
    }

    #endregion

    #region 磁盘同步

    /// <summary> 将内存数据同步写入磁盘（真正同步，非异步） </summary>
    private void FlushToDisk()
    {
        if (!_dirty) return;
        SaveData data = new SaveData();

        foreach (var kv in _ints)
            data.intValues.Add(new KVPair<int> { k = kv.Key, v = kv.Value });
        foreach (var kv in _floats)
            data.floatValues.Add(new KVPair<float> { k = kv.Key, v = kv.Value });
        foreach (var kv in _strings)
            data.stringValues.Add(new KVPair<string> { k = kv.Key, v = kv.Value });

        string json = JsonUtility.ToJson(data);
        WriteFileText(SaveFilePath, json);
        _dirty = false;
    }

    #endregion

    #region 基础 API（保持与旧版完全兼容）

    public void SetInt(string key, int value)
    {
        if (AdManager.NoAdsEnabled) { LocalNoAdsSaveStore.Instance.SetInt(key, value); return; }
        EnsureInitialized();
        _ints[key] = value;
        _dirty = true;
    }

    public int GetInt(string key, int defaultValue = 0)
    {
        if (AdManager.NoAdsEnabled) return UnityEngine.PlayerPrefs.GetInt(key, defaultValue);
        EnsureInitialized();
        return _ints.TryGetValue(key, out int val) ? val : defaultValue;
    }

    public void SetFloat(string key, float value)
    {
        if (AdManager.NoAdsEnabled) { LocalNoAdsSaveStore.Instance.SetFloat(key, value); return; }
        EnsureInitialized();
        _floats[key] = value;
        _dirty = true;
    }

    public float GetFloat(string key, float defaultValue = 0f)
    {
        if (AdManager.NoAdsEnabled) return UnityEngine.PlayerPrefs.GetFloat(key, defaultValue);
        EnsureInitialized();
        return _floats.TryGetValue(key, out float val) ? val : defaultValue;
    }

    public void SetString(string key, string value)
    {
        if (AdManager.NoAdsEnabled) { LocalNoAdsSaveStore.Instance.SetString(key, value); return; }
        EnsureInitialized();
        _strings[key] = value;
        _dirty = true;
    }

    public string GetString(string key, string defaultValue = "")
    {
        if (AdManager.NoAdsEnabled) return LocalNoAdsSaveStore.Instance.GetString(key, defaultValue);
        EnsureInitialized();
        return _strings.TryGetValue(key, out string val) ? val : defaultValue;
    }

    public void SetBool(string key, bool value)
    {
        SetInt(key, value ? 1 : 0);
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        return GetInt(key, defaultValue ? 1 : 0) == 1;
    }

    public bool HasKey(string key)
    {
        if (AdManager.NoAdsEnabled) return LocalNoAdsSaveStore.Instance.HasKey(key);
        EnsureInitialized();
        return _ints.ContainsKey(key) || _floats.ContainsKey(key) || _strings.ContainsKey(key);
    }

    public void DeleteKey(string key)
    {
        if (AdManager.NoAdsEnabled) { LocalNoAdsSaveStore.Instance.DeleteKey(key); return; }
        EnsureInitialized();
        _ints.Remove(key);
        _floats.Remove(key);
        _strings.Remove(key);
        _dirty = true;
    }

    public void DeleteAll()
    {
        if (AdManager.NoAdsEnabled) { LocalNoAdsSaveStore.Instance.DeleteAll(); return; }
        EnsureInitialized();
        _ints.Clear();
        _floats.Clear();
        _strings.Clear();
        _dirty = true;
        // 同时删除磁盘文件确保干净状态
        DeleteFile(SaveFilePath);
    }

    /// <summary> 保存：同步写入磁盘（不再是异步 PlayerPrefs.Save） </summary>
    public void Save()
    {
        if (AdManager.NoAdsEnabled) { LocalNoAdsSaveStore.Instance.Save(); return; }
        EnsureInitialized();
        FlushToDisk();
    }

    #endregion

    #region 扩展 API（JSON 序列化，内部走 SetString/GetString，无需改动）

    public void SetJson<T>(string key, T obj) where T : class
    {
        try
        {
            if (AdManager.NoAdsEnabled) { LocalNoAdsSaveStore.Instance.SetJson(key, JsonUtility.ToJson(obj)); return; }
            string json = UnityEngine.JsonUtility.ToJson(obj);
            SetString(key, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[TTSaveManager] JSON序列化失败 {key}: {e.Message}");
        }
    }

    public T GetJson<T>(string key, T defaultValue = null) where T : class
    {
        if (!HasKey(key))
            return defaultValue;

        string json = GetString(key, "");
        if (string.IsNullOrEmpty(json))
            return defaultValue;

        try
        {
            return UnityEngine.JsonUtility.FromJson<T>(json) ?? defaultValue;
        }
        catch (Exception e)
        {
            Debug.LogError($"[TTSaveManager] JSON反序列化失败 {key}: {e.Message}");
            return defaultValue;
        }
    }

    public void SetJsonArray<T>(string key, T[] array) where T : class
    {
        try
        {
            if (AdManager.NoAdsEnabled) { LocalNoAdsSaveStore.Instance.SetJson(key, JsonHelper.ToJson(array)); return; }
            string json = JsonHelper.ToJson(array);
            SetString(key, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[TTSaveManager] JSON数组序列化失败 {key}: {e.Message}");
        }
    }

    public T[] GetJsonArray<T>(string key, T[] defaultValue = null) where T : class
    {
        if (!HasKey(key))
            return defaultValue;

        string json = GetString(key, "");
        if (string.IsNullOrEmpty(json))
            return defaultValue;

        try
        {
            return JsonHelper.FromJson<T>(json, defaultValue);
        }
        catch (Exception e)
        {
            Debug.LogError($"[TTSaveManager] JSON数组反序列化失败 {key}: {e.Message}");
            return defaultValue;
        }
    }

    #endregion
}

/// <summary>
/// JsonHelper - 用于序列化数组（Unity 自带的 JsonUtility 不支持数组）
/// </summary>
public static class JsonHelper
{
    [Serializable]
    private class Wrapper<T>
    {
        public T[] items;
    }

    public static string ToJson<T>(T[] array)
    {
        Wrapper<T> wrapper = new Wrapper<T> { items = array };
        return UnityEngine.JsonUtility.ToJson(wrapper);
    }

    public static T[] FromJson<T>(string json, T[] defaultValue = null)
    {
        Wrapper<T> wrapper = UnityEngine.JsonUtility.FromJson<Wrapper<T>>(json);
        return wrapper?.items ?? defaultValue ?? new T[0];
    }
}
