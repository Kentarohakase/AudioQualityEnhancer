# AudioQualityEnhancer

AudioQualityEnhancer ist ein kleines Windows Desktop Tool für die Analyse, schonende Bearbeitung und den Export von Audio mit FFmpeg und FFprobe. Die App ist als C# WPF Anwendung mit MVVM-Struktur gebaut und zielt auf .NET 10.

Das Tool macht keine falschen Qualitätsversprechen: Es kann Audio normalisieren, filtern, entrauschen und in sinnvolle Zielformate exportieren. Es kann aber keine Informationen zurückholen, die durch schlechte Aufnahme, Clipping oder verlustbehaftete Kompression bereits zerstört wurden.

## Features

- Audio- und Videodateien mit Audiospur öffnen
- Drag and Drop und Dateiauswahl
- FFprobe-Analyse als JSON
- Anzeige von Codec, Bitrate, Sample Rate, Kanälen, Dauer, Container und Dateigröße
- Hinweis, ob die Quelle wahrscheinlich verlustbehaftet ist
- Presets für Musik, Sprache, Rauschreduzierung, verlustfreie Extraktion, Archiv und Alltag
- Ausgabe als WAV 24 Bit, FLAC, MP3 320k, AAC 256k, Opus 160k oder Opus 192k
- Stream-Copy für verlustfreie Extraktion, wenn der Quellcodec sinnvoll kopierbar ist
- Keine direkte Überschreibung der Originaldatei
- Automatisch eindeutige Ausgabedateinamen
- Temporäre Ausgabedatei mit sauberem Move erst nach erfolgreichem FFmpeg-Lauf
- Async-Prozessausführung ohne UI-Freeze
- Sichtbares UI-Log und optional gespeicherte Logdatei
- FFmpeg-stderr wird geloggt, aber nicht automatisch als Fehler gewertet
- Erfolg oder Fehler wird über den Exit Code geprüft

## Was das Tool nicht kann

Dieses Tool kann Audio verbessern, normalisieren und restaurieren, aber keine Informationen zurückholen, die durch schlechte Aufnahme oder verlustbehaftete Kompression bereits zerstört wurden.

Eine MP3-Datei wird durch Export nach FLAC nicht besser. FLAC ist trotzdem sinnvoll, wenn eine bereits bearbeitete Datei ohne weitere Exportverluste archiviert werden soll.

## Screenshots

Screenshots können später hier ergänzt werden.

## Voraussetzungen

- Windows
- .NET 10 SDK
- FFmpeg und FFprobe
- Optional: GitHub CLI, wenn das Repository direkt per `gh` erstellt und gepusht werden soll

## FFmpeg und FFprobe installieren

Variante 1: Installation über Winget:

```powershell
winget install Gyan.FFmpeg
```

Variante 2: Manuell installieren:

1. FFmpeg für Windows herunterladen.
2. `ffmpeg.exe` und `ffprobe.exe` in einen Ordner entpacken.
3. Den `bin`-Ordner zur Windows `PATH` Umgebungsvariable hinzufügen.
4. Neues Terminal öffnen und prüfen:

```powershell
ffmpeg -version
ffprobe -version
```

Alternativ können `ffmpeg.exe` und `ffprobe.exe` bewusst neben die gebaute App gelegt werden. Sie werden nicht in dieses Repository committed.

## Build

```powershell
dotnet build
```

## Start

```powershell
dotnet run
```

## Release EXE erstellen

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish/win-x64
```

Die erzeugte EXE liegt danach in `publish/win-x64`. FFmpeg und FFprobe müssen weiterhin installiert sein oder bewusst neben die EXE gelegt werden.

## Nutzung

1. Audio- oder Videodatei auswählen oder per Drag and Drop ablegen.
2. Analyse prüfen.
3. Preset wählen.
4. Ausgabeformat und Ausgabeordner wählen.
5. Verarbeitung starten.
6. Fortschritt und FFmpeg-Log im UI beobachten.

Unterstützte Eingaben:

- `mp3`
- `wav`
- `flac`
- `m4a`
- `aac`
- `ogg`
- `opus`
- `mp4`
- `mkv`

Bei Videodateien wird nur die erste Audiospur verarbeitet.

## Presets

### Musik verbessern

Normalisiert Musik ungefähr auf `-14 LUFS`, `TP=-1.5 dB`, `LRA=11`. Es wird keine aggressive Rauschreduzierung angewendet.

### Sprache verbessern

Setzt einen High-Pass bei 80 Hz und normalisiert ungefähr auf `-16 LUFS`, `TP=-1.5 dB`, `LRA=11`. Optional können leichte Kompression und eine dezente Anhebung im Präsenzbereich aktiviert werden.

### Rauschen reduzieren

Verwendet `afftdn` mit einstellbarem Noise Floor. Zu starke Rauschreduzierung kann metallisch oder künstlich klingen.

### Nur verlustfrei extrahieren

Extrahiert die erste Audiospur ohne Bearbeitung mit `-c:a copy`, wenn ein passender Container sinnvoll bestimmt werden kann.

### Archiv Export

Exportiert als FLAC. FLAC ist verlustfrei, stellt aber keine bereits verlorenen Details wieder her.

### Alltag Export

Für alltagstaugliche Dateien sind AAC 256k, MP3 320k oder Opus 160k/192k vorgesehen.

## Exportformate

- WAV 24 Bit: `-c:a pcm_s24le`
- FLAC: `-c:a flac -compression_level 8`
- MP3 320k: `-c:a libmp3lame -b:a 320k`
- AAC 256k: `-c:a aac -b:a 256k`
- Opus 160k: `-c:a libopus -b:a 160k -vbr on`
- Opus 192k: `-c:a libopus -b:a 192k -vbr on`

## Projektstruktur

```text
AudioQualityEnhancer/
  Models/
    AudioInfo.cs
    AudioPreset.cs
    ExportFormat.cs
    ProcessResult.cs
    ProcessingOptions.cs
    Result.cs
  Services/
    AudioProcessingService.cs
    FFmpegService.cs
    FFprobeService.cs
    FileNameService.cs
    LogService.cs
  ViewModels/
    AsyncRelayCommand.cs
    MainViewModel.cs
    RelayCommand.cs
  Views/
    MainWindow.xaml
    MainWindow.xaml.cs
  App.xaml
  App.xaml.cs
  AudioQualityEnhancer.csproj
  GlobalUsings.cs
  README.md
  .gitignore
```

## Sicherheit und Dateien

- Originaldateien werden nie überschrieben.
- Wenn ein Zielname bereits existiert, wird automatisch ein neuer Name erzeugt.
- Während der Verarbeitung wird eine temporäre Datei erzeugt und erst nach erfolgreichem FFmpeg-Lauf verschoben.
- Logs speichern keine absichtlich gesetzten Tokens, API Keys oder Passwörter.
- Es werden keine Zugangsdaten gespeichert.
- Build Outputs, Logs, temporäre Dateien, erzeugte Audiodateien und FFmpeg-Binaries sind per `.gitignore` ausgeschlossen.

## GitHub Repository

Geplantes Repository:

```text
https://github.com/Kentarohakase/AudioQualityEnhancer
```

Wenn GitHub CLI funktioniert:

```powershell
gh repo create Kentarohakase/AudioQualityEnhancer --public --source=. --remote=origin --push
```

Manuell:

```powershell
git branch -M main
git remote add origin https://github.com/Kentarohakase/AudioQualityEnhancer.git
git push -u origin main
```

## Lizenz

Dieses Projekt steht unter der MIT-Lizenz. Details stehen in `LICENSE`.

## Bekannte Grenzen

- FFmpeg-Filter können hörbare Artefakte erzeugen, besonders bei starker Rauschreduzierung.
- `loudnorm` wird in dieser Version als einfacher Ein-Pass-Filter verwendet. Für streng broadcast-konforme Workflows wäre eine Zwei-Pass-Loudness-Normalisierung genauer.
- Es wird die erste Audiospur verarbeitet. Mehrspur-Auswahl ist nicht eingebaut.
- Videobilder werden nicht exportiert.
- Clipping, stark beschädigte Aufnahmen und verlorene Codec-Details können nicht vollständig repariert werden.
- Die Qualität hängt stark von Quelle, Codec, Aufnahmezustand und gewähltem Exportformat ab.
