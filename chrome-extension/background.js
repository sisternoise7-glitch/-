const OFFSCREEN_URL = 'offscreen.html';
const SITE_HOSTS = ['gogoanime.it.com', 'gogoanime.by'];

let active = null;
let creatingOffscreen = null;
const lastCaption = new Map();

function isSupportedSite(url) {
  try {
    const host = new URL(url).hostname.toLowerCase();
    return SITE_HOSTS.some((domain) => host === domain || host.endsWith(`.${domain}`));
  } catch (_) {
    return false;
  }
}

async function setAction(tabId, badge = '', color = '#475569', title = '애니 오디오 한글자막 시작/중지') {
  await Promise.allSettled([
    chrome.action.setBadgeText({ tabId, text: badge }),
    chrome.action.setBadgeBackgroundColor({ tabId, color }),
    chrome.action.setTitle({ tabId, title }),
  ]);
}

async function ensureOffscreen() {
  const contexts = await chrome.runtime.getContexts({
    contextTypes: ['OFFSCREEN_DOCUMENT'],
    documentUrls: [chrome.runtime.getURL(OFFSCREEN_URL)],
  });
  if (contexts.length) return;
  if (!creatingOffscreen) {
    creatingOffscreen = chrome.offscreen.createDocument({
      url: OFFSCREEN_URL,
      reasons: ['USER_MEDIA', 'AUDIO_PLAYBACK'],
      justification: '사용자가 선택한 Chrome 탭의 소리를 로컬 자막 프로그램으로 전달하고, 재생 소리를 유지합니다.',
    }).finally(() => { creatingOffscreen = null; });
  }
  await creatingOffscreen;
}

async function sendOffscreen(message) {
  let lastError;
  for (let attempt = 0; attempt < 4; attempt += 1) {
    try {
      await chrome.runtime.sendMessage({ target: 'audio-bridge-offscreen', ...message });
      return;
    } catch (error) {
      lastError = error;
      await new Promise((resolve) => setTimeout(resolve, 70));
    }
  }
  throw lastError || new Error('오디오 처리 페이지에 연결하지 못했어요.');
}

async function sendPage(tabId, message) {
  try { await chrome.tabs.sendMessage(tabId, message); } catch (_) { }
}

async function ensurePage(tabId) {
  try { await chrome.scripting.executeScript({ target: { tabId }, files: ['content.js'] }); } catch (_) { }
}

async function stop(reason = 'stopped', tellOffscreen = true) {
  const previous = active;
  active = null;
  if (tellOffscreen) {
    try { await sendOffscreen({ type: 'bridge-stop', reason }); } catch (_) { }
  }
  if (previous?.tabId) {
    lastCaption.delete(previous.tabId);
    await sendPage(previous.tabId, { type: 'bridge-clear' });
    await setAction(previous.tabId);
  }
}

async function start(tab) {
  if (!tab?.id) return;
  if (!isSupportedSite(tab.url || '')) {
    await setAction(tab.id, '!', '#b91c1c', 'gogoanime 영상 탭에서 눌러주세요.');
    return;
  }
  if (active?.tabId === tab.id) {
    await stop('user-stop');
    return;
  }
  if (active) await stop('switch-tab');

  try {
    await ensurePage(tab.id);
    await ensureOffscreen();
    const { sourceLanguage = 'ja' } = await chrome.storage.local.get({ sourceLanguage: 'ja' });
    // 탭 오디오 권한은 사용자가 확장 아이콘을 누른 직후에만 얻는다.
    const streamId = await chrome.tabCapture.getMediaStreamId({ targetTabId: tab.id });
    active = { tabId: tab.id, sourceLanguage, state: 'preparing' };
    await setAction(tab.id, '…', '#4f46e5', '탭 오디오를 준비하는 중…');
    await sendPage(tab.id, { type: 'bridge-state', state: 'preparing', message: '탭 오디오를 준비하는 중…' });
    await sendOffscreen({ type: 'bridge-start', tabId: tab.id, streamId, sourceLanguage });
  } catch (error) {
    const message = String(error?.message || error || '탭 오디오를 시작하지 못했어요.');
    active = null;
    await setAction(tab.id, '!', '#b91c1c', message);
    await sendPage(tab.id, { type: 'bridge-state', state: 'error', message });
  }
}

chrome.action.onClicked.addListener((tab) => { void start(tab); });

chrome.runtime.onMessage.addListener((message) => {
  if (message?.source !== 'audio-bridge-offscreen') return;
  if (!active || message.tabId !== active.tabId) return;

  if (message.type === 'bridge-state') {
    active.state = message.state;
    const title = message.message || '애니 오디오 한글자막';
    if (message.state === 'error') void setAction(active.tabId, '!', '#b91c1c', title);
    else if (message.state === 'model-download') void setAction(active.tabId, '↓', '#4f46e5', title);
    else if (message.state === 'stopped') { void stop('stream-stopped', false); return; }
    else void setAction(active.tabId, 'ON', '#0f8a4b', title);
    void sendPage(active.tabId, { type: 'bridge-state', state: message.state, message: message.message });
    return;
  }

  if (message.type === 'bridge-caption' && message.korean) {
    const payload = { type: 'bridge-caption', korean: message.korean, original: message.original || '' };
    lastCaption.set(active.tabId, payload);
    void sendPage(active.tabId, payload);
  }
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (tabId !== active?.tabId || changeInfo.status !== 'complete') return;
  void (async () => {
    await ensurePage(tabId);
    await sendPage(tabId, { type: 'bridge-state', state: active?.state || 'listening', message: '탭 오디오를 듣는 중…' });
    const caption = lastCaption.get(tabId);
    if (caption) await sendPage(tabId, caption);
  })();
});

chrome.tabs.onRemoved.addListener((tabId) => {
  if (tabId === active?.tabId) void stop('tab-closed');
});

chrome.tabCapture.onStatusChanged.addListener((info) => {
  if (info.tabId === active?.tabId && (info.status === 'stopped' || info.status === 'error')) {
    void stop(`capture-${info.status}`, false);
  }
});
