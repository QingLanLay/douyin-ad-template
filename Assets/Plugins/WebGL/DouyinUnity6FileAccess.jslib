// 部分抖音 UnityPlugin 的文件访问钩子仍依赖 SYSCALLS.doAccess。
// Unity 6000 的 Emscripten 3.1.39 将这段逻辑内联到了 __syscall_faccessat。
// 在链接期补回缺失的辅助函数；已有原生实现时不覆盖，也不跳过真实权限检查。
(function () {
  var syscalls = LibraryManager.library.$SYSCALLS;
  if (!syscalls || typeof syscalls !== 'object' || typeof syscalls.doAccess === 'function') {
    return;
  }
  syscalls.doAccess = function (path, amode) {
    if (amode & ~7) {
      return -28; // EINVAL
    }
    var lookup = FS.lookupPath(path, { follow: true });
    var node = lookup.node;
    if (!node) {
      return -44; // ENOENT
    }
    var perms = '';
    if (amode & 4) perms += 'r';
    if (amode & 2) perms += 'w';
    if (amode & 1) perms += 'x';
    if (perms && FS.nodePermissions(node, perms)) {
      return -2; // EACCES
    }
    return 0;
  };
})();
