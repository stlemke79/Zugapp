# Diktat (Windows)

Diktieren in jedes Windows-Programm, mit lokaler Spracherkennung (Whisper) und optionaler Überarbeitung durch Claude.

## So funktioniert's

1. **Strg + Alt + D** drücken. Rechts oben auf dem Monitor erscheint ein pulsierendes rotes Mikrofon.
2. Sprechen.
3. Nochmal **Strg + Alt + D** drücken. Das Mikrofon wird grau mit einem drehenden Ring, solange Erkennung und KI arbeiten. **Esc** bricht ab.
4. Der Text landet automatisch dort, wo du gerade schreibst (Word, Outlook, Browser, Teams …).
   Ist kein Eingabefeld aktiv, öffnet sich ein eigenes Fenster mit dem Text zum Bearbeiten und Kopieren.

Das Programm läuft unsichtbar mit einem Mikrofon-Symbol im Infobereich unten rechts.
- **Rechtsklick** aufs Symbol: Einstellungen, Verlauf, letztes Diktat kopieren, Beenden
- **Linksklick** aufs Symbol: Verlauf

## Spracherkennung

- Läuft **lokal und offline** mit [whisper.cpp](https://github.com/ggerganov/whisper.cpp). Die Aufnahmen verlassen den PC nicht.
- Beim ersten Start wird das Modell einmalig heruntergeladen, standardmäßig „small“ mit 466 MB.
  In den Einstellungen lässt sich „base“ (schneller) oder „large-v3-turbo“ (genauer, braucht einen schnellen PC) wählen.
- Unter „Namen / Fachbegriffe“ kannst du Wörter eintragen, die oft falsch erkannt werden.

## KI-Nachbearbeitung (Claude)

Ist sie eingeschaltet, wird der erkannte Text vor dem Einfügen von Claude überarbeitet:

- Zeichensetzung, Rechtschreibung und Grammatik werden korrigiert.
- Füllwörter („äh“, „ähm“), Versprecher und Selbstkorrekturen werden entfernt („am Montag, nein, Dienstag“ → „am Dienstag“).
- Gesprochene Befehle werden umgesetzt: „neuer Absatz“, „Komma“, „Fragezeichen“ …
- Gewünschte Formate werden umgesetzt: „… als Stichpunkte“, „… als E-Mail“, „… förmlich“.
- Diktierte Fragen oder Bitten werden **nicht** beantwortet, sondern nur als Text überarbeitet.

Dafür braucht es einen **Claude-API-Schlüssel** von [console.anthropic.com](https://console.anthropic.com).
Er wird unter Einstellungen eingetragen und mit Windows-Bordmitteln (DPAPI) verschlüsselt nur für dein Benutzerkonto gespeichert.
Dabei wird der erkannte **Text** (nicht die Audioaufnahme) an die Claude-API geschickt.
Ohne Schlüssel oder bei einem Fehler wird der unbearbeitete Text eingefügt.

Standardmodell ist `claude-opus-5-5` mit niedrigem Denkaufwand. Schneller und günstiger ist `claude-haiku-5-5`, das lässt sich in den Einstellungen umstellen.

## Installieren

1. Im Repository rechts auf **Releases** klicken (oder https://github.com/stlemke79/Zugapp/releases/latest öffnen).
2. **DiktatApp-win-Setup.exe** herunterladen und ausführen. Das Programm wird für dein Benutzerkonto installiert, Administratorrechte sind nicht nötig.
   Danach gibt es einen Eintrag im Startmenü, und unter Windows-Einstellungen → Apps lässt es sich deinstallieren.
3. Windows SmartScreen meldet eventuell „Unbekannter Herausgeber“, weil das Programm nicht signiert ist. Dann auf **Weitere Informationen → Trotzdem ausführen** klicken.

Unter Einstellungen → „Mit Windows starten“ startet das Programm automatisch.

## Automatische Updates

Jeder Push auf GitHub erzeugt über GitHub Actions eine neue Version als Release (1.0.1, 1.0.2 …).
Diktat sucht beim Start und danach alle 4 Stunden nach einer neuen Version und lädt sie im Hintergrund herunter.
Installiert wird sie, sobald du gerade nicht diktierst: Diktat startet dann kurz neu und meldet sich mit „Diktat wurde aktualisiert“.
Über Rechtsklick → **Nach Updates suchen** geht es auch sofort.

Das funktioniert nur, weil das Repository öffentlich ist. Das Programm lädt die Updates ohne Anmeldung herunter.

## Gut zu wissen

- **Programme mit Administratorrechten**: Windows erlaubt nicht, dass ein normales Programm dort Text einfügt.
  Dann erscheint der Text im eigenen Fenster.
- Die Zwischenablage wird kurz zum Einfügen benutzt und danach wieder hergestellt.
- Einstellungen, Verlauf und Protokoll liegen unter `%APPDATA%\Diktat`, die Sprachmodelle unter `%LOCALAPPDATA%\Diktat\models`.

## Entwicklung

```
dotnet build src/Diktat/Diktat.csproj
dotnet publish src/Diktat/Diktat.csproj -c Release -r win-x64 --self-contained -o publish
```

Aufbau:

| Datei | Aufgabe |
|---|---|
| `TrayApp.cs` | Ablaufsteuerung: Tastenkürzel → Aufnahme → Erkennung → KI → Einfügen |
| `Core/AudioRecorder.cs` | Mikrofonaufnahme (16 kHz mono, NAudio) |
| `Core/Transcriber.cs` | Whisper-Spracherkennung und Modell-Download |
| `Core/AiPostProcessor.cs` | Überarbeitung mit Claude (Anthropic C# SDK) |
| `Core/FocusDetector.cs` | Erkennt, ob gerade ein Eingabefeld aktiv ist (Schreibmarke und UI Automation) |
| `Core/Updater.cs` | Auto-Update über Velopack und GitHub-Releases |
| `Core/TextInserter.cs` | Einfügen per Zwischenablage + Strg+V, danach Zwischenablage wiederherstellen |
| `Ui/*` | Statusanzeige, Ergebnisfenster, Einstellungen, Verlauf, Download |
