# AudioQualityEnhancer

AudioQualityEnhancer ist ein kleines Windows-Tool, das Audiodateien analysiert und mit FFmpeg sauber aufbereitet. Es ist für einfache Fälle gedacht: Datei reinziehen, Profil wählen, exportieren.

Wichtig: Das Tool kann Audio besser klingen lassen, Lautheit anpassen und störende Bereiche reduzieren. Es kann aber keine Details zurückholen, die durch schlechte Aufnahme, Clipping oder MP3-Kompression schon verloren sind.

## Was kann die App?

- Audio- und Videodateien mit Audiospur öffnen
- Datei per Button oder Drag and Drop auswählen
- Codec, Bitrate, Sample Rate, Kanäle, Dauer und Dateigröße anzeigen
- Musik oder Sprache per Preset verbessern
- Rauschen vorsichtig reduzieren
- Audiospur ohne Re-Encoding extrahieren, wenn es sinnvoll möglich ist
- Original und Ergebnis direkt anhören
- Als WAV, FLAC, MP3, AAC, Opus oder Premiere-Pro-Profil exportieren
- FFmpeg/FFprobe automatisch im App-Ordner, im `Tools`-Ordner oder im Windows `PATH` finden

## Was kann die App nicht?

- Aus einer schlechten Aufnahme keine perfekte Studioaufnahme machen
- Aus MP3 durch FLAC wieder echte verlorene Qualität herstellen
- Clipping oder stark zerstörtes Material vollständig reparieren
- Mehrere Audiospuren aus Videos manuell auswählen

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

Bei Videodateien wird aktuell die erste Audiospur verwendet.

## Profile

### Musik verbessern

Normalisiert Musik auf eine sinnvolle Lautheit, ohne sie unnötig hart zu komprimieren.

### Sprache verbessern

Filtert tiefe Störgeräusche, hebt Sprache bei Bedarf leicht an und normalisiert sie für bessere Verständlichkeit.

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
dotnet run
```

## Release bauen

Portable ZIP ohne FFmpeg:

```powershell
.\scripts\package-release.ps1 -Version 0.2.1
```

Portable ZIP mit FFmpeg und FFprobe:

```powershell
.\scripts\package-release.ps1 -Version 0.2.1 -IncludeFFmpeg
```

Die fertigen ZIP-Dateien liegen danach in `artifacts`.

## Sicherheit

- Die Originaldatei wird nicht überschrieben.
- Wenn eine Zieldatei schon existiert, wird automatisch ein neuer Name erzeugt.
- Temporäre Dateien werden nur während der Verarbeitung genutzt.
- Logs sollen bei normalen Fehlern helfen, speichern aber keine Zugangsdaten.
- FFmpeg-Binaries und erzeugte Audiodateien werden nicht committed.

## Lizenz

MIT License. Details stehen in `LICENSE`.
