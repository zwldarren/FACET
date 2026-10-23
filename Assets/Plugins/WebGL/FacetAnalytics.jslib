// FACET analytics: post one url-encoded form submission to a Google Form from the WebGL player.
// mode 'no-cors' because Google does not send CORS headers: the browser still delivers the request
// (a simple POST with a safelisted content type needs no preflight), it only hides the reply, which
// a form submission does not need. keepalive lets the last wave's row leave even if the tab closes.
mergeInto(LibraryManager.library, {
  FacetAnalyticsPost: function (urlPtr, bodyPtr) {
    var url = UTF8ToString(urlPtr);
    var body = UTF8ToString(bodyPtr);
    try {
      fetch(url, {
        method: 'POST',
        mode: 'no-cors',
        keepalive: true,
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: body
      }).catch(function (e) { console.warn('FACET analytics: ' + e); });
    } catch (e) {
      console.warn('FACET analytics: ' + e);
    }
  }
});
