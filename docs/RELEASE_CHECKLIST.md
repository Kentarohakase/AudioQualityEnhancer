# Release Checklist

Diese Checkliste hilft dabei, Releases gleichbleibend sauber zu erstellen.

## Vor dem Release

- `dotnet build .\AudioQualityEnhancer.slnx -c Release`
- `dotnet test .\AudioQualityEnhancer.slnx -c Release --no-build`
- `.\scripts\package-release.ps1 -Version <version>`
- `.\scripts\verify-release-package.ps1 -Version <version>`
- Changelog auf die neue Version setzen.
- README-Beispiele auf die neue Version aktualisieren.
- Release Notes kurz, neutral und produktbezogen formulieren.
- Keine lokalen Pfade, Zugangsdaten, Tokens oder internen Arbeitsnotizen aufnehmen.

## GitHub Release

- Tag auf dem Release-Commit setzen.
- `main` und den Tag pushen.
- GitHub Release als normalen oeffentlichen Release erstellen.
- Release Workflow beobachten.
- Beide ZIP-Dateien pruefen:
  - `AudioQualityEnhancer-<version>-win-x64.zip`
  - `AudioQualityEnhancer-<version>-win-x64-with-ffmpeg.zip`
- Beide ZIP-Dateien muessen `THIRD_PARTY_NOTICES.md` enthalten.
- Das ZIP mit FFmpeg muss `Tools/FFMPEG_VERSION.txt`, `Tools/ffmpeg.exe` und `Tools/ffprobe.exe` enthalten.
- Nach dem Workflow kann das heruntergeladene Paket erneut mit `.\scripts\verify-release-package.ps1 -Version <version> -RequireFFmpegPackage` geprueft werden.

## Nach dem Release

- `CHANGELOG.md` wieder mit `## Unreleased` oeffnen.
- CI auf `main` pruefen.
- Keine erzeugten Audio-, Log-, Report-, Temp- oder Build-Dateien committen.
