async function load() {
  const { sourceLanguage = 'ja', allowExternalTranslation = true } = await chrome.storage.local.get({ sourceLanguage: 'ja', allowExternalTranslation: true });
  const input = document.querySelector(`input[name="language"][value="${sourceLanguage}"]`);
  if (input) input.checked = true;
  document.getElementById('external-translation').checked = allowExternalTranslation;
}

document.getElementById('save').addEventListener('click', async () => {
  const sourceLanguage = document.querySelector('input[name="language"]:checked')?.value || 'ja';
  const allowExternalTranslation = document.getElementById('external-translation').checked;
  await chrome.storage.local.set({ sourceLanguage, allowExternalTranslation });
  document.getElementById('status').textContent = '저장했어요. 다음 시작부터 적용됩니다.';
});

void load();
