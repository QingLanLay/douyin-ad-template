mergeInto(LibraryManager.library, {
    $DouyinNoAdsFileSync: { running: false, pending: false },
    DouyinNoAdsSyncFiles__deps: ['$FS', '$DouyinNoAdsFileSync'],
    DouyinNoAdsSyncFiles: function () {
        if (DouyinNoAdsFileSync.running) {
            DouyinNoAdsFileSync.pending = true;
            return;
        }
        DouyinNoAdsFileSync.running = true;
        var flush = function () {
            DouyinNoAdsFileSync.pending = false;
            FS.syncfs(false, function (error) {
                if (error) console.error('[无广告存档] IndexedDB 同步失败', error);
                if (DouyinNoAdsFileSync.pending) flush();
                else DouyinNoAdsFileSync.running = false;
            });
        };
        flush();
    }
});
