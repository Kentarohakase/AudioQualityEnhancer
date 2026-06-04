# AudioQualityEnhancer

AudioQualityEnhancer ist ein kleines Windows-Tool, das Audiodateien analysiert und mit FFmpeg sauber aufbereitet. Es ist für einfache Fälle gedacht: Datei reinziehen, Profil wählen, exportieren.

Wichtig: Das Tool kann Audio besser klingen lassen, Lautheit anpassen und störende Bereiche reduzieren. Es kann aber keine Details zurückholen, die durch schlechte Aufnahme, Clipping oder MP3-Kompression schon verloren sind.

## Was kann die App?

- Audio- und Videodateien mit Audiospur öffnen
- Datei per Button oder Drag and Drop auswählen
- Audiospur bei Videoquellen auswählen, wenn mehrere Spuren vorhanden sind
- Codec, Bitrate, Sample Rate, Kanäle, Dauer und Dateigröße anzeigen
- Lautheit, Peak-Werte und mögliche technische Probleme optional genauer messen
- passende Presets und Ausgabeformate anhand der Analyse vorschlagen
- Musik oder Sprache per Preset verbessern
- Podcast- und Voiceover-Spuren mit eigenen Sprach-Presets aufbereiten
- das gewählte Preset als kurze A/B-Vorschau rendern und vor dem Export anhören
- Rauschen vorsichtig reduzieren
- Audiospur ohne Re-Encoding extrahieren, wenn es sinnvoll möglich ist
- Original und Ergebnis direkt anhören
- Ergebnis nach dem Export technisch prüfen und als Bericht speichern
- Ausgabeordner, letzte Ausgabedatei und Log direkt aus der App öffnen oder kopieren
- Als WAV, FLAC, MP3, AAC, Opus oder Premiere-Pro-Profil exportieren
- Mehrere Dateien in einer Warteschlange nacheinander verarbeiten
- FFmpeg/FFprobe automatisch im App-Ordner, im `Tools`-Ordner oder im Windows `PATH` finden

## Was kann die App nicht?

- Aus einer schlechten Aufnahme keine perfekte Studioaufnahme machen
- Aus MP3 durch FLAC wieder echte verlorene Qualität herstellen
- Clipping oder stark zerstörtes Material vollständig reparieren
- stark beschädigte oder fehlende Audiospuren automatisch ersetzen

## Unterstützte Dateien

Eingabe:

- MP3
- WAV
- FLAC
- M4A / AAC
- OGG
- Opus
- MP4
- MKV

Bei Videodateien wird die erste Audiospur vorausgewählt. Wenn mehrere Audiospuren vorhanden sind, kannst du pro Datei die gewünschte Spur auswählen.

## Analyse

Die normale Analyse ist schnell und liest Metadaten mit FFprobe. Zusätzlich gibt es eine erweiterte Analyse mit FFmpeg. Sie misst Lautheit, True Peak, Sample Peak, Durchschnittspegel und Dynamikbereich. Das hilft bei Hinweisen auf Clipping, sehr niedrige Bitrate, verdächtige Sample Rate, wenig Headroom, sehr leises Material, Mono-/Mehrkanalquellen und mögliche Transcoding-Fallen.

Die App bewertet diese Daten zusätzlich mit einem einfachen Score, technischen Hinweisen und Empfehlungen. Das hilft bei der Wahl eines sinnvollen Presets. Der Score bewertet technische Auffälligkeiten, nicht subjektive Klangqualität, und verspricht keine Wiederherstellung verlorener Details.

## Empfohlene Einstellungen

Nach der Analyse schlägt die App passende Presets und Ausgabeformate vor. Die Vorschläge basieren auf technischen Hinweisen wie Bitrate, Sample Rate, Kanalzahl, Container, Headroom und Peak-Werten. Sie werden nicht automatisch angewendet; du kannst einen Vorschlag bewusst übernehmen.

Die Beratung erkennt Musik oder Sprache nicht sicher. Sie erklärt nur, welche Einstellung aufgrund der Messwerte wahrscheinlich sinnvoll ist.

## Batch-Verarbeitung

Du kannst mehrere Dateien per Button oder Drag and Drop hinzufügen. Die App analysiert jede Datei und zeigt sie in einer Warteschlange an. Beim Start werden die bereiten Dateien nacheinander mit demselben Preset, Ausgabeformat und Ausgabeordner verarbeitet.

Wenn eine Datei fehlschlägt, läuft die Warteschlange weiter. Abbrechen stoppt nur die laufende Verarbeitung; noch offene Dateien bleiben in der Liste.

Nach einem Batch-Lauf kannst du die Warteschlange nach Status filtern, Einträge mit Warnungen oder Fehlern schneller finden und fehlgeschlagene oder abgebrochene Dateien erneut vorbereiten. Für die ausgewählte Datei lassen sich die erzeugte Ausgabedatei und ihr Ausgabeordner direkt öffnen.

## Ergebnisprüfung

Nach einem Export prüft die App die erzeugte Datei noch einmal. Sie vergleicht technische Werte wie Dauer, Codec, Sample Rate, Kanalzahl, Lautheit und Peaks mit der Quelle und dem gewählten Ausgabeformat. Dadurch sieht man schneller, ob der Export plausibel ist oder ob zum Beispiel Clipping, wenig Headroom, eine auffällige Dauerabweichung oder eine unerwartete Formatänderung entstanden ist.

Diese Prüfung bewertet Messwerte, nicht den subjektiven Klang. Sie kann helfen, Fehler zu finden, macht aus schlechtem Ausgangsmaterial aber keine verlorenen Details wieder hörbar.

## Profile

### Musik verbessern

Normalisiert Musik auf eine sinnvolle Lautheit, ohne sie unnötig hart zu komprimieren.

### Sprache verbessern

Filtert tiefe Störgeräusche, hebt Sprache bei Bedarf leicht an und normalisiert sie für bessere Verständlichkeit.

### Podcast-Stimme

Bereitet Sprache für Podcasts und Voiceovers klarer auf. Das Preset reduziert Mumpf vorsichtig, hebt Verständlichkeit moderat an, entschärft Zischlaute und normalisiert auf eine gut hörbare Lautheit.

### Rauschige Sprache bereinigen

Kombiniert vorsichtige Rauschreduzierung mit Sprachbearbeitung. Das ist für begrenzte oder verrauschte Sprachaufnahmen gedacht und bleibt bewusst konservativ, damit Stimmen nicht metallisch klingen.

### Rauschen reduzieren

Reduziert Grundrauschen mit vorsichtigen Standardwerten. Zu starke Rauschreduzierung kann künstlich klingen.

### Nur verlustfrei extrahieren

Kopiert die Audiospur ohne Bearbeitung, wenn der Codec und Container dazu passen.

### Archiv Export

Speichert als FLAC. Das ist gut zum Aufbewahren nach der Bearbeitung, macht eine MP3 aber nicht besser als vorher.

### Alltag Export

Für fertige Dateien mit guter Qualität und vernünftiger Dateigröße.

## Ausgabeformate

- WAV 24 Bit
- FLAC
- MP3 320k
- AAC 256k
- Opus 160k
- Opus 192k
- Premiere Pro

Das Premiere-Pro-Profil exportiert als `WAV 24 Bit / 48 kHz`. Das ist groß, aber verlustfrei und für den Videoschnitt deutlich besser geeignet als MP3.

## FFmpeg installieren

Am einfachsten mit Winget:

```powershell
winget install Gyan.FFmpeg
```

Danach prüfen:

```powershell
ffmpeg -version
ffprobe -version
```

Portable Nutzung geht auch:

1. EXE entpacken.
2. `ffmpeg.exe` und `ffprobe.exe` in den Ordner `Tools` legen.
3. App starten.

Die App sucht FFmpeg in dieser Reihenfolge:

1. neben `AudioQualityEnhancer.exe`
2. im Ordner `Tools`
3. im Windows `PATH`

## Start aus dem Quellcode

```powershell
dotnet build
dotnet test
dotnet run
```

Die Tests prüfen Parser, Dateilogik, Batch-Logik, Analyse-Bewertung, FFmpeg-Argumentdarstellung, Ergebnisprüfung, Exportprofile und Sprachressourcen. Sie starten keine FFmpeg-Prozesse.

## Release bauen

Portable ZIP ohne FFmpeg:

```powershell
.\scripts\package-release.ps1 -Version 0.12.5
```

Portable ZIP mit FFmpeg und FFprobe:

```powershell
.\scripts\package-release.ps1 -Version 0.12.5 -IncludeFFmpeg
```

Pakete prüfen:

```powershell
.\scripts\verify-release-package.ps1 -Version 0.12.5 -RequireFFmpegPackage
```

Die fertigen ZIP-Dateien liegen danach in `artifacts`.

## Third-party software

AudioQualityEnhancer nutzt FFmpeg und FFprobe als externe Programme. Die Tools gehören zum FFmpeg-Projekt und unterliegen ihren eigenen Lizenzbedingungen. Details stehen in `THIRD_PARTY_NOTICES.md`.

## Sicherheit

- Die Originaldatei wird nicht überschrieben.
- Wenn eine Zieldatei schon existiert, wird automatisch ein neuer Name erzeugt.
- Temporäre Dateien werden nur während der Verarbeitung genutzt.
- Logs sollen bei normalen Fehlern helfen, speichern aber keine Zugangsdaten.
- FFmpeg-Binaries und erzeugte Audiodateien werden nicht committed.

## Lizenz

MIT License. Details stehen in `LICENSE`.
