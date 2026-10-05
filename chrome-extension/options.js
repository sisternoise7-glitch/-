async function load() {
  const { sourceLanguage = 'ja' } = await chrome.storage.local.get({ sourceLanguage: 'ja' });
  const input = document.querySelector(`input[name="language"][value="${sourceLanguage}"]`);
  if (input) input.checked = true;
}

document.getElementById('save').addEventListener('click', async () => {
  const sourceLanguage = document.querySelector('input[name="language"]:checked')?.value || 'ja';
  await chrome.storage.local.set({ sourceLanguage });
  document.getElementById('status').textContent = '저장했어요. 다음 시작부터 적용됩니다.';
});

void load();
