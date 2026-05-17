# Smoke Test

Diese Tests sind fuer echte Medien-Dateien gedacht und werden nicht automatisiert.

## Vorbereitung

- App aus dem aktuellen Release-ZIP starten.
- FFmpeg und FFprobe muessen gefunden werden.
- Einen leeren Ausgabeordner verwenden.

## Einzeldateien

- MP3 laden, analysieren und als AAC oder FLAC exportieren.
- WAV oder FLAC laden, mit Musik-Preset exportieren.
- Sprachdatei laden, Sprache-Preset verwenden und Ergebnis anhoeren.
- Rauschreduzierung mit vorsichtigem Wert testen.

## Videoquellen

- MP4 oder MKV mit Audiospur laden.
- Falls mehrere Audiospuren vorhanden sind, eine andere Spur auswaehlen.
- Export starten und pruefen, ob die gewaehlte Spur verarbeitet wurde.

## Batch

- Mehrere Dateien hinzufuegen.
- Eine Datei mit anderer Audiospur auswaehlen.
- Warteschlange starten.
- Pruefen, ob Fehler bei einer Datei die restliche Warteschlange nicht stoppen.

## Ergebnispruefung

- Ergebnisstatus in der Warteschlange pruefen.
- Vorher/Nachher-Werte ansehen.
- Berichtdatei im Ausgabeordner oeffnen.
- Bei Warnungen die Datei kurz anhoeren und Messwerte plausibilisieren.

## Abschluss

- Originaldateien muessen unveraendert bleiben.
- Keine temporaren Dateien im Projekt committen.
- Keine erzeugten Medien-, Log- oder Report-Dateien committen.
