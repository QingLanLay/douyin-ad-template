using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>无广告模式本地存档：标量用 Unity PlayerPrefs，JSON 用独立文件；不访问抖音 SDK。</summary>
internal sealed class LocalNoAdsSaveStore
{
    private static LocalNoAdsSaveStore instance;
    internal static LocalNoAdsSaveStore Instance => instance ??= new LocalNoAdsSaveStore();

    [Serializable]
    private sealed class JsonEntry
    {
        public string key;
        public string json;
    }

    [Serializable]
    private sealed class LocalData
    {
        public List<string> playerPrefsKeys = new List<string>();
        public List<JsonEntry> jsonValues = new List<JsonEntry>();
    }

    private readonly string path;
    private LocalData data;
    private bool dirty;
    private bool invalidPrimary;

    private LocalNoAdsSaveStore(string filePath = null)
    {
        path = filePath ?? Path.Combine(Application.persistentDataPath, "noads-local.json");
        data = ReadData(path);
        if (data != null) return;
        invalidPrimary = File.Exists(path);
        data = ReadData(path + ".bak") ?? new LocalData();
        if (invalidPrimary)
            Debug.LogWarning("[无广告存档] 本地 JSON 损坏，使用备份或默认值；读取时不覆盖原文件。");
    }

    private static LocalData ReadData(string file)
    {
        if (!File.Exists(file)) return null;
        try
        {
            string json = File.ReadAllText(file, Encoding.UTF8);
            // 防止任意 JSON 对象被当作有效但空的存档。
            if (string.IsNullOrEmpty(json) || !json.Contains("\"playerPrefsKeys\"") || !json.Contains("\"jsonValues\""))
                return null;
            LocalData result = JsonUtility.FromJson<LocalData>(json);
            if (result == null || result.playerPrefsKeys == null || result.jsonValues == null) return null;
            result.jsonValues.RemoveAll(entry => entry == null || string.IsNullOrEmpty(entry.key));
            result.playerPrefsKeys.RemoveAll(string.IsNullOrEmpty);
            return result;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[无广告存档] 读取失败：{exception.Message}");
            return null;
        }
    }

    private void TrackScalar(string key)
    {
        data.jsonValues.RemoveAll(entry => entry.key == key);
        if (!data.playerPrefsKeys.Contains(key)) data.playerPrefsKeys.Add(key);
        dirty = true;
    }

    internal void SetInt(string key, int value)
    {
        PlayerPrefs.SetInt(key, value);
        TrackScalar(key);
    }

    internal void SetFloat(string key, float value)
    {
        PlayerPrefs.SetFloat(key, value);
        TrackScalar(key);
    }

    internal void SetString(string key, string value)
    {
        PlayerPrefs.SetString(key, value ?? string.Empty);
        TrackScalar(key);
    }

    internal string GetString(string key, string defaultValue)
    {
        JsonEntry entry = data.jsonValues.Find(item => item.key == key);
        return entry != null ? entry.json : PlayerPrefs.GetString(key, defaultValue);
    }

    internal bool HasKey(string key) => PlayerPrefs.HasKey(key) || data.jsonValues.Exists(entry => entry.key == key);

    internal void SetJson(string key, string json)
    {
        // 同一个 key 重新写入另一种类型时，保持 Unity PlayerPrefs 的覆盖语义。
        PlayerPrefs.DeleteKey(key);
        data.playerPrefsKeys.Remove(key);
        JsonEntry entry = data.jsonValues.Find(item => item.key == key);
        if (entry == null) data.jsonValues.Add(new JsonEntry { key = key, json = json });
        else entry.json = json;
        dirty = true;
    }

    internal void DeleteKey(string key)
    {
        PlayerPrefs.DeleteKey(key);
        data.playerPrefsKeys.Remove(key);
        data.jsonValues.RemoveAll(entry => entry.key == key);
        dirty = true;
    }

    internal void DeleteAll()
    {
        // 只清除经 TTSaveManager 写入的本地 key，不清空其他系统的 PlayerPrefs，也不触碰 SDK 存档。
        foreach (string key in data.playerPrefsKeys) PlayerPrefs.DeleteKey(key);
        data.playerPrefsKeys.Clear();
        data.jsonValues.Clear();
        dirty = true;
    }

    internal void Save()
    {
        PlayerPrefs.Save();
        if (!dirty) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(data), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                // 损坏文件单独保留；不能覆盖已用于恢复的有效备份。
                File.Copy(path, path + (invalidPrimary ? ".corrupt" : ".bak"), true);
            }
            File.Copy(temp, path, true);
            File.Delete(temp);
            dirty = false;
            invalidPrimary = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            // 普通 WebGL 的 System.IO 写入还需同步到浏览器 IndexedDB。
            DouyinNoAdsSyncFiles();
#endif
        }
        catch (Exception exception)
        {
            Debug.LogError($"[无广告存档] 本地写入失败，保留待保存数据：{exception.Message}");
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void DouyinNoAdsSyncFiles();
#endif
}
