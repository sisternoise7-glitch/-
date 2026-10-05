(() => {
  if (globalThis.__animeAudioBridgeOverlayV1) return;
  globalThis.__animeAudioBridgeOverlayV1 = true;

  const ROOT_ID = 'anime-audio-bridge-overlay-v1';
  let root;
  let caption;
  let status;
  let statusTimer = 0;

  function mount() {
    if (root?.isConnected) return;
    root = document.getElementById(ROOT_ID);
    if (!root) {
      root = document.createElement('div');
      root.id = ROOT_ID;
      root.innerHTML = '<div class="caption" data-caption></div><div class="status" data-status></div>';
      const style = document.createElement('style');
      style.textContent = `
        #${ROOT_ID}{position:fixed;inset:0;z-index:2147483647;pointer-events:none;font-family:"Malgun Gothic","Apple SD Gothic Neo",sans-serif}
        #${ROOT_ID} .caption{position:absolute;left:50%;bottom:8%;transform:translateX(-50%);max-width:min(86vw,1100px);padding:10px 18px;border-radius:10px;background:rgba(0,0,0,.84);box-shadow:0 2px 14px rgba(0,0,0,.6);color:#fff;font-size:clamp(18px,2.1vw,34px);font-weight:750;line-height:1.42;text-align:center;text-shadow:0 1px 3px #000;opacity:0;transition:opacity .14s ease;white-space:pre-wrap}
        #${ROOT_ID} .caption.show{opacity:1}
        #${ROOT_ID} .status{position:absolute;left:50%;bottom:3%;transform:translateX(-50%);max-width:84vw;padding:6px 12px;border-radius:999px;background:rgba(17,24,39,.92);color:#eef2ff;font-size:13px;line-height:1.35;text-align:center;opacity:0;transition:opacity .14s ease}
        #${ROOT_ID} .status.show{opacity:1} #${ROOT_ID} .status.error{background:rgba(127,29,29,.96);color:#fee2e2}
      `;
      root.appendChild(style);
      document.documentElement.appendChild(root);
    }
    caption = root.querySelector('[data-caption]');
    status = root.querySelector('[data-status]');
  }

  function showStatus(message, kind = '') {
    mount();
    clearTimeout(statusTimer);
    status.textContent = message || '';
    status.className = `status show ${kind}`.trim();
    if (kind !== 'error' && message) statusTimer = setTimeout(() => status.classList.remove('show'), 4200);
  }

  function clear() {
    mount();
    caption.textContent = '';
    caption.classList.remove('show');
    status.textContent = '';
    status.className = 'status';
  }

  chrome.runtime.onMessage.addListener((message) => {
    if (!message?.type) return;
    if (message.type === 'bridge-caption') {
      mount();
      caption.textContent = message.korean || '';
      caption.classList.toggle('show', Boolean(message.korean));
      return;
    }
    if (message.type === 'bridge-clear') {
      clear();
      return;
    }
    if (message.type === 'bridge-state') {
      if (message.state === 'error') showStatus(message.message || '음성 자막 오류', 'error');
      else if (message.state === 'stopped') clear();
      else if (message.message) showStatus(message.message);
    }
  });
})();
