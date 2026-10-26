// 仅适配抖音 Unity6000 正常 main 返回：不得吞掉显式退出、非零错误或真正的运行时关闭。
(function () {
  var library = LibraryManager.library;
  if (typeof library.$exitJS !== 'function' || library.$GfbDouyinOriginalExitJS) {
    return;
  }
  // 保留当前 Emscripten 的完整实现与依赖（包括调试、线程和退出清理分支）。
  var dependencies = (library.$exitJS__deps || []).slice();
  library.$GfbDouyinOriginalExitJS = library.$exitJS;
  library.$GfbDouyinOriginalExitJS__deps = dependencies;
  library.$exitJS__deps = dependencies.concat(['$GfbDouyinOriginalExitJS']);
  library.$exitJS = function (status, implicit) {
    if (implicit === true && status === 0 && keepRuntimeAlive() &&
        !(typeof ENVIRONMENT_IS_PTHREAD !== 'undefined' && ENVIRONMENT_IS_PTHREAD) &&
        typeof GameGlobal === 'object' && GameGlobal && GameGlobal.unityNamespace &&
        GameGlobal.unityNamespace.unityVersion === '6000.0.46f1' && Module['StarkSYSCALLS']) {
      EXITSTATUS = status;
      return;
    }
    return GfbDouyinOriginalExitJS(status, implicit);
  };
})();
