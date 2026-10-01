# Traduce

Native Windows app: C# 5, WinForms, .NET Framework 4.8, Windows OCR. No npm or runtime framework bundle.

Keep Alt+T region selection, local OCR before image fallback, Spanish ↔ other-language translation, cancellation, clipboard preservation, and the compact result window. Its outer width follows the selection and it stays visible above/below the crop across monitors, without taking focus from the source app. Keep explanatory controls in Settings, not in the translation result.

All user-facing UI strings must support Spanish and English through `L.T` and `assets/en.json`; never localize captured text, provider IDs, log fields or translation instructions. If a label changes language, size its control to fit both translations. Keep README.md (English) and README.es.md (Spanish) in sync. Brand assets live in assets; regenerate the multi-size ICO with scripts/make-icon.ps1. Documentation screenshots come from the running app: dist/Traduce.Tests.exe --screenshots, with the installed app closed.

If a provider is added or changed, verify its current official API/CLI documentation and test both text and image contracts. Use the user's unmodified official CLI for account access; never harvest OAuth tokens. Keys use Windows DPAPI CurrentUser, never logs or plaintext configuration. Do not contact a paid service in ordinary tests.

Build: `./build.ps1 -ContractTest` for CI; `./build.ps1 -Test` for local Windows UI/OCR tests. Run `-DesktopTest` only on an unlocked desktop with the installed app closed. `-LiveTest` explicitly sends three Codex requests and needs an authenticated account. Package with `./package.ps1 -SkipBuild` after a successful build (Inno Setup 6 required).

Do not commit dist, release binaries, credentials, user settings or capture logs. Distribute binaries through GitHub Releases. Inspect diffs and validate only affected behavior, repeating when a change/failure requires it. Commits: `type(scope): concrete description`.
