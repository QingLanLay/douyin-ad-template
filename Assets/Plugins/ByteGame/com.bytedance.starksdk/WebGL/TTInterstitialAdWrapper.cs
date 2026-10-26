using System;

namespace TTSDK
{
    /// <summary>插屏统一包装。无广告判断在 SDK 包装内部完成，避免调用创建、加载或展示委托。</summary>
    public static class TTInterstitialAdWrapper
    {
        /// <summary>由业务层提供当前 noAds 状态，SDK 程序集不反向引用 AdManager。</summary>
        public static Func<bool> NoAdsProvider { get; set; }

        public static bool IsNoAds => NoAdsProvider != null && NoAdsProvider();

        /// <summary>无广告时完成继续回调一次并返回 true；正常模式不执行回调。</summary>
        public static bool TrySkip(Action onClose = null)
        {
            if (!IsNoAds) return false;
            onClose?.Invoke();
            return true;
        }

        /// <summary>延迟创建真实 SDK 实例。无广告时不执行工厂，直接返回 null。</summary>
        public static T Create<T>(Func<T> create) where T : class
        {
            if (IsNoAds) return null;
            if (create == null) throw new ArgumentNullException(nameof(create));
            return create();
        }

        /// <summary>
        /// 无广告时不调用展示委托，直接完成继续回调。
        /// 返回 true 仅表示调用了真实展示；false 表示正常跳过，不是广告失败。
        /// 正常模式仍由原 SDK 的 OnClose 完成业务回调，不提前伪造关闭。
        /// </summary>
        public static bool TryShow(Action show, Action onClose = null)
        {
            if (TrySkip(onClose)) return false;
            if (show == null) throw new ArgumentNullException(nameof(show));
            show();
            return true;
        }
    }
}
