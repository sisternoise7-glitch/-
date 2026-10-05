# Code signing policy

Free code signing provided by SignPath.io, certificate by SignPath Foundation.

## Team roles

This is a single-maintainer open-source project.

- **Author, committer, reviewer, and release approver:** [sisternoise7-glitch](https://github.com/sisternoise7-glitch)
- Changes proposed by people other than the maintainer must be reviewed by the maintainer before merge.
- Every release-signing request is manually approved by the release approver after reviewing the source revision, build workflow, and release artifact.

## What may be signed

Only the Windows release package built by the GitHub Actions workflow in this repository may be submitted for signing. The signed executable is `AnimeAudioCaptioner.exe`; third-party libraries included in its package are not submitted as this project's binaries.

Release builds must originate from the `main` branch, use GitHub-hosted Windows runners, and be traceable to a public commit in this repository.

## Privacy

The project's data handling is described in [PRIVACY.md](PRIVACY.md). The Chrome extension provides a one-time option to disable the external text-translation fallback.
