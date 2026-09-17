// Demo layout only. No wallet state or private material crosses this boundary.
mergeInto(LibraryManager.library, {
  BlockmakerDemoViewportWidth: function () {
    var box = Module.canvas && Module.canvas.getBoundingClientRect();
    return Math.max(1, Math.round(box && box.width || window.innerWidth));
  },
  BlockmakerDemoViewportHeight: function () {
    var box = Module.canvas && Module.canvas.getBoundingClientRect();
    return Math.max(1, Math.round(box && box.height || window.innerHeight));
  }
});
