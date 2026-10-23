// FACET page: the browser's fullscreen, asked from inside the player.
// The shell's pause card draws its "leave fullscreen" row from FacetPageFullscreen and presses it
// through FacetPageLeaveFullscreen; both forward to window.facetPage, which the template
// (Assets/WebGLTemplates/FACET/index.html) publishes. A build served in some other page has no such
// object, and both answers say "windowed" and "nothing to do" rather than throwing at the player.
mergeInto(LibraryManager.library, {
  FacetPageFullscreen: function () {
    return (window.facetPage && window.facetPage.fullscreen()) ? 1 : 0;
  },
  FacetPageLeaveFullscreen: function () {
    if (window.facetPage) window.facetPage.leaveFullscreen();
  }
});
