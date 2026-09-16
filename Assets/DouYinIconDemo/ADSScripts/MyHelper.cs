using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;

public class MyHelper
{
    public static T Create<T>(bool HavePar=false) where T : MonoBehaviour
    {
        T t = null;
        var obj = MonoBehaviour.FindObjectOfType(typeof(T));
        if (obj != null)
            t = obj as T;
        if (t == null)
        {
            var go = new GameObject(typeof(T).Name);
           
            t = go.AddComponent<T>();
            MonoBehaviour.DontDestroyOnLoad(go);
        }
        return t;
    }
    public static string EventSystemCurrentObj()
    {
        if (EventSystem.current.currentSelectedGameObject != null)
            return EventSystem.current.currentSelectedGameObject.name;
        return "";
    }
    public static Transform FindChild(Transform trans, string goName)
    {
        Transform child = trans.Find(goName);
        if (child != null) return child;
        Transform go;
        for (int i = 0; i < trans.childCount; i++)
        {
            go = FindChild(trans.GetChild(i), goName);
            if (go != null) return go;
        }
        return null;
    }
    public static void LookAtTarget(Vector3 target,
        Transform trans, float rotationSpeed)
    {
        if (target != Vector3.zero)
        {
            Quaternion dir = Quaternion.LookRotation(target);
            trans.rotation = Quaternion.Lerp(trans.rotation, dir,
                rotationSpeed * Time.deltaTime);
        }
    }
    public static T FindChild<T>(Transform trans, string childName) where T : Component
    {
        Transform goTF = FindChild(trans, childName);
        if (goTF == null)
            return null;
        return goTF.GetComponent<T>();
    }
    /// <summary>
    /// 记录当前时间戳到指定key，用于后续 HasElapsedSeconds 判断时间间隔
    /// </summary>
    /// <param name="key">存储键名</param>
    public static void RecordTimestamp(string key)
    {
        System.DateTime startTime = TimeZone.CurrentTimeZone.ToLocalTime(new System.DateTime(1970, 1, 1)); // 当地时区
        long time = (long)(DateTime.Now - startTime).TotalMilliseconds; // 相差毫秒数
        TTSaveManager.Instance.SetString(key, time.ToString());
        TTSaveManager.Instance.Save();
    }

    /// <summary>
    /// 判断距离上次 RecordTimestamp 记录的时间是否已超过指定秒数
    /// </summary>
    /// <param name="key">存储键名</param>
    /// <param name="seconds">指定秒数阈值</param>
    /// <returns>已超过返回true，否则false。首次调用（无记录）返回true</returns>
    public static bool HasElapsedSeconds(string key, float seconds)
    {
        System.DateTime startTime = TimeZone.CurrentTimeZone.ToLocalTime(new System.DateTime(1970, 1, 1)); // 当地时区
        long time = (long)(DateTime.Now - startTime).TotalMilliseconds; // 相差毫秒数
        long beforeTime = long.Parse(TTSaveManager.Instance.GetString(key, "0"));
        if (beforeTime != 0)
        {
            int elapsedSec = (int)Mathf.Ceil((time - beforeTime) / 1000);
            Debug.Log($"[MyHelper] HasElapsedSeconds key={key}, elapsed={elapsedSec}s, threshold={seconds}s");
            if (elapsedSec >= seconds || elapsedSec == 0)
            {
                return true;
            }
            return false;
        }
        Debug.Log($"[MyHelper] HasElapsedSeconds key={key}, no previous record, return true");
        return true;
    }

    // 保留旧中文方法名兼容旧调用，标记为过时
    [System.Obsolete("Use RecordTimestamp instead", false)]
    public static void 计时(string key) => RecordTimestamp(key);

    [System.Obsolete("Use HasElapsedSeconds instead", false)]
    public static bool 是否超过指定秒(string key, float sTime) => HasElapsedSeconds(key, sTime);
    // 点积
    public static float Dot(Vector2 v1, Vector2 v2) => v1.x * v2.x + v1.y * v2.y;

    // 叉积（二维向量叉积为标量）
    public static float Cross(Vector2 v1, Vector2 v2) => v1.x * v2.y - v2.x * v1.y;

    // 判断两个向量是否同向
    public static bool IsSameDirection(Vector2 v1, Vector2 v2, double epsilon = 1e-9)
    {
        // 零向量无方向，直接返回false
        //if (v1.IsZero || v2.IsZero)
        //    return false;

        // 1. 判断共线（叉积绝对值小于精度阈值）
        bool isCollinear = Math.Abs(Cross(v1, v2)) < epsilon;
        if (!isCollinear)
            return false;

        // 2. 判断方向相同（点积大于0）
        bool isSameDir = Dot(v1, v2) > epsilon;

        return isCollinear && isSameDir;
    }
    // 判断是否点击UI
    public static  bool IsPointerOverUI()
    {
        if (Input.touchCount > 0)
            return EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
        else
            return EventSystem.current.IsPointerOverGameObject();
    }
}
