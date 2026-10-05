const BRIDGE_URL = 'ws://127.0.0.1:38495/';
let capture = null;
let translator = null;
let translatorLanguage = '';

function tell(type, data = {}, target = capture) {
  if (!target?.tabId) return;
  chrome.runtime.sendMessage({ source: 'audio-bridge-offscreen', type, tabId: target.tabId, ...data }).catch(() => {});
}

function state(name, message, target = capture) {
  tell('bridge-state', { state: name, message }, target);
}

function sourceLanguage(language) {
  return language === 'en' ? 'en' : 'ja';
}

async function createTranslator(language, target) {
  const source = sourceLanguage(language);
  if (translator && translatorLanguage === source) return translator;
  if (translator?.destroy) translator.destroy();
  translator = null;
  translatorLanguage = '';

  const TranslatorApi = globalThis.Translator;
  if (!TranslatorApi?.availability || !TranslatorApi?.create) return null;
  try {
    const availability = await TranslatorApi.availability({ sourceLanguage: source, targetLanguage: 'ko' });
    if (availability === 'unavailable') return null;
    if (availability !== 'available') state('model-download', 'Chrome 번역 모델을 준비하는 중…', target);
    translator = await TranslatorApi.create({
      sourceLanguage: source,
      targetLanguage: 'ko',
      monitor(monitor) {
        monitor.addEventListener('downloadprogress', (event) => {
          state('model-download', `Chrome 번역 모델 다운로드 ${Math.round((event.loaded || 0) * 100)}%`, target);
        });
      },
    });
    translatorLanguage = source;
    return translator;
  } catch (_) {
    return null;
  }
}

async function fallbackTranslate(text, language) {
  const url = new URL('https://translate.googleapis.com/translate_a/single');
  url.searchParams.set('client', 'gtx');
  url.searchParams.set('sl', sourceLanguage(language));
  url.searchParams.set('tl', 'ko');
  url.searchParams.set('dt', 't');
  url.searchParams.set('q', text);
  const response = await fetch(url);
  if (!response.ok) throw new Error(`번역 서버 응답 ${response.status}`);
  const data = await response.json();
  const korean = Array.isArray(data?.[0]) ? data[0].map((part) => part?.[0]).filter(Boolean).join('').trim() : '';
  if (!korean) throw new Error('번역 결과가 비어 있어요.');
  return korean;
}

async function translateTranscript(original, target) {
  const token = ++target.translationToken;
  try {
    const local = await createTranslator(target.sourceLanguage, target);
    const korean = local ? await local.translate(original) : await fallbackTranslate(original, target.sourceLanguage);
    if (capture !== target || !target.running || token !== target.translationToken || !korean) return;
    tell('bridge-caption', { original, korean: String(korean).trim() }, target);
    state('listening', '탭 오디오를 듣는 중…', target);
  } catch (error) {
    if (capture === target && target.running) state('error', `번역 오류: ${String(error?.message || error)}`, target);
  }
}

function floatToPcm16(input, inputRate) {
  const outputLength = Math.max(1, Math.round(input.length * 16000 / inputRate));
  const output = new Int16Array(outputLength);
  const ratio = input.length / outputLength;
  for (let index = 0; index < outputLength; index += 1) {
    const start = Math.floor(index * ratio);
    const end = Math.min(input.length, Math.max(start + 1, Math.floor((index + 1) * ratio)));
    let total = 0;
    for (let source = start; source < end; source += 1) total += input[source];
    const value = Math.max(-1, Math.min(1, total / (end - start)));
    output[index] = value < 0 ? value * 0x8000 : value * 0x7fff;
  }
  return new Uint8Array(output.buffer);
}

function bytesToBase64(bytes) {
  let binary = '';
  const chunk = 0x4000;
  for (let start = 0; start < bytes.length; start += chunk) {
    binary += String.fromCharCode(...bytes.subarray(start, Math.min(bytes.length, start + chunk)));
  }
  return btoa(binary);
}

function connectBridge(target) {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(BRIDGE_URL);
    const timeout = setTimeout(() => {
      try { socket.close(); } catch (_) { }
      reject(new Error('프로그램 연결 시간이 지났어요. Anime Audio Captioner.exe를 먼저 실행해 주세요.'));
    }, 3500);
    socket.addEventListener('open', () => {
      clearTimeout(timeout);
      if (capture !== target || !target.running) { socket.close(); return; }
      target.socket = socket;
      socket.send(JSON.stringify({ type: 'start', language: sourceLanguage(target.sourceLanguage) }));
      state('connected', 'EXE 연결됨 — 탭 소리를 확인하는 중…', target);
      resolve();
    }, { once: true });
    socket.addEventListener('error', () => {
      clearTimeout(timeout);
      reject(new Error('Anime Audio Captioner.exe에 연결하지 못했어요. 프로그램을 먼저 실행해 주세요.'));
    }, { once: true });
    socket.addEventListener('message', (event) => {
      if (capture !== target || !target.running) return;
      let message;
      try { message = JSON.parse(event.data); } catch (_) { return; }
      if (message.type === 'transcript' && message.text) void translateTranscript(String(message.text), target);
      if (message.type === 'state' && message.state === 'error') state('error', message.detail || 'EXE 처리 오류', target);
    });
    socket.addEventListener('close', () => {
      if (capture === target && target.running && !target.stopping) {
        state('error', 'EXE 연결이 끊겼어요.', target);
      }
    });
  });
}

async function startCapture({ tabId, streamId, sourceLanguage: language }) {
  await stopCapture({ notify: false });
  const target = {
    tabId,
    sourceLanguage: language === 'en' ? 'en' : 'ja',
    running: true,
    stopping: false,
    stream: null,
    audioContext: null,
    source: null,
    worklet: null,
    socket: null,
    signalSeen: false,
    translationToken: 0,
  };
  capture = target;
  state('preparing', '탭 오디오를 연결하는 중…', target);

  try {
    // 먼저 streamId를 소비해야 Chrome의 짧은 만료 시간 안에 탭 오디오를 얻을 수 있다.
    const stream = await navigator.mediaDevices.getUserMedia({
      audio: { mandatory: { chromeMediaSource: 'tab', chromeMediaSourceId: streamId } },
      video: false,
    });
    if (capture !== target || !target.running) { stream.getTracks().forEach((track) => track.stop()); return; }

    const AudioContextCtor = globalThis.AudioContext || globalThis.webkitAudioContext;
    if (!AudioContextCtor) throw new Error('Chrome 오디오 출력을 준비하지 못했어요.');
    const audioContext = new AudioContextCtor();
    await audioContext.audioWorklet.addModule(chrome.runtime.getURL('audio-worklet.js'));
    const source = audioContext.createMediaStreamSource(stream);
    const worklet = new AudioWorkletNode(audioContext, 'pcm-tap');
    source.connect(worklet);
    worklet.connect(audioContext.destination);
    await audioContext.resume();

    target.stream = stream;
    target.audioContext = audioContext;
    target.source = source;
    target.worklet = worklet;
    stream.getAudioTracks()[0]?.addEventListener('ended', () => {
      if (capture === target && target.running) void stopCapture();
    }, { once: true });

    await connectBridge(target);
    void createTranslator(target.sourceLanguage, target);
    worklet.port.onmessage = (event) => {
      if (capture !== target || !target.running || target.socket?.readyState !== WebSocket.OPEN) return;
      const samples = new Float32Array(event.data);
      let energy = 0;
      for (const sample of samples) energy += Math.abs(sample);
      if (!target.signalSeen && energy / samples.length > 0.006) {
        target.signalSeen = true;
        state('audio-detected', '탭 소리 입력 확인됨 — 음성을 인식 중…', target);
      }
      if (target.socket.bufferedAmount > 512 * 1024) return;
      const pcm = floatToPcm16(samples, audioContext.sampleRate);
      target.socket.send(JSON.stringify({
        type: 'audio',
        pcm16: bytesToBase64(pcm),
        sampleRate: 16000,
        language: sourceLanguage(target.sourceLanguage),
      }));
    };
  } catch (error) {
    if (capture === target) {
      state('error', String(error?.message || error || '탭 오디오 오류'), target);
      await stopCapture({ notify: false });
    }
  }
}

async function stopCapture({ notify = true } = {}) {
  const previous = capture;
  capture = null;
  if (!previous) return;
  previous.running = false;
  previous.stopping = true;
  try { previous.socket?.send(JSON.stringify({ type: 'stop' })); } catch (_) { }
  try { previous.socket?.close(); } catch (_) { }
  try { previous.worklet?.disconnect(); } catch (_) { }
  try { previous.source?.disconnect(); } catch (_) { }
  try { await previous.audioContext?.close(); } catch (_) { }
  try { previous.stream?.getTracks().forEach((track) => track.stop()); } catch (_) { }
  if (notify) state('stopped', '음성 한글자막을 중지했어요.', previous);
}

chrome.runtime.onMessage.addListener((message) => {
  if (message?.target !== 'audio-bridge-offscreen') return;
  if (message.type === 'bridge-start') void startCapture(message);
  if (message.type === 'bridge-stop') void stopCapture();
});
