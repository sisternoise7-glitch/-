# Privacy policy

Last updated: 2026-10-05

## What stays on this computer

`AnimeAudioCaptioner.exe` receives the audio stream only through the local address `127.0.0.1`. The extension sends the selected Chrome tab's audio to the EXE on the same computer. It does not upload video, screenshots, page contents, or raw audio to this project or to the website being viewed.

Speech recognition is performed locally with Whisper. The first time the EXE runs, it downloads the selected Whisper model (about 148 MB) from the model source used by the `Whisper.net` package; this is required before local recognition can begin.

## Translation choices

The extension first tries Chrome's built-in Translator API. Chrome may download a translation model when that feature is used, subject to Chrome's own policies.

If Chrome's Translator API is unavailable, the extension can use Google Translate as a fallback. In that case, **only the recognized text** is sent to `translate.googleapis.com` for Japanese/English-to-Korean translation. Raw audio and video are not sent. This fallback is enabled by default so captions can work on more Chrome installations, and can be disabled at any time in the extension's Options page before starting captions.

## Local data and retention

The EXE keeps the downloaded model in the current Windows user's local application-data folder. It does not create an account, collect analytics, or retain recognized captions after the active session. Chrome stores the selected language and external-translation preference in the extension's local storage.

## Contact

For privacy questions or removal requests, open an issue in this repository.
