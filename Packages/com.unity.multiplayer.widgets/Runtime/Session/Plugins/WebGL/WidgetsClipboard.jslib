// Browser clipboard for the join-code copy button (WebGL). GUIUtility.systemCopyBuffer never reaches the browser's
// clipboard, and browsers only allow copying during a real user gesture. Unity handles clicks a frame after the
// browser event, so the button arms the copy on pointer down and the copy runs in the browser's own pointerup /
// touchend handler, inside the gesture. execCommand('copy') is tried first: it also works in cross-origin iframes
// (itch.io) where navigator.clipboard may be blocked by the page's permissions policy.
mergeInto(LibraryManager.library, {
  WidgetsArmClipboardCopy: function (textPtr) {
    var text = UTF8ToString(textPtr);

    function copyWithExecCommand() {
      var area = document.createElement('textarea');
      area.value = text;
      area.setAttribute('readonly', '');
      area.style.position = 'fixed';
      area.style.top = '0';
      area.style.left = '0';
      area.style.opacity = '0';
      document.body.appendChild(area);
      area.focus();
      area.select();
      var ok = false;
      try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
      document.body.removeChild(area);
      var canvas = document.querySelector('canvas');
      if (canvas) canvas.focus(); // give keyboard input back to the game
      return ok;
    }

    function copyNow() {
      if (copyWithExecCommand()) return;
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).catch(function (e) {
          console.warn('[Widgets] Could not copy the join code: ' + e);
        });
      }
    }

    function disarm() {
      var pending = window.__widgetsCopyOnRelease;
      if (!pending) return;
      window.removeEventListener('pointerup', pending, true);
      window.removeEventListener('touchend', pending, true);
      window.__widgetsCopyOnRelease = null;
    }

    function onRelease() {
      disarm();
      copyNow();
    }

    disarm(); // only the latest press copies
    window.__widgetsCopyOnRelease = onRelease;
    window.addEventListener('pointerup', onRelease, true);
    window.addEventListener('touchend', onRelease, true);
  }
});
