# Zugapp Diktat (Windows)

Diktieren in jedes Windows-Programm, mit lokaler Spracherkennung (Whisper) und optionaler Überarbeitung durch Claude.

## So funktioniert's

1. **Strg + Alt + D** drücken. Unten am Bildschirm erscheint „Aufnahme läuft …“.
2. Sprechen.
3. Nochmal **Strg + Alt + D** drücken. **Esc** bricht ab.
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

## Herunterladen und starten

Bei jedem Push baut GitHub Actions das Programm:

1. Im Repository auf **Actions** → **Windows-Build** → den neuesten Lauf klicken.
2. Unten unter **Artifacts** auf **Zugapp-Diktat-win-x64** klicken. Es wird eine ZIP-Datei heruntergeladen.
3. Die ZIP-Datei entpacken, z. B. nach `C:\Programme\Zugapp` oder in einen Ordner unter „Dokumente“, und **Zugapp.exe** starten.
   .NET muss nicht installiert sein.
4. Beim ersten Start meldet Windows SmartScreen eventuell „Unbekannter Herausgeber“. Dann auf **Weitere Informationen → Trotzdem ausführen** klicken.
   Der Grund: Das Programm ist noch nicht signiert.

Unter Einstellungen → „Mit Windows starten“ startet das Programm automatisch.

## Gut zu wissen

- **Programme mit Administratorrechten**: Windows erlaubt nicht, dass ein normales Programm dort Text einfügt.
  Dann erscheint der Text im eigenen Fenster.
- Die Zwischenablage wird kurz zum Einfügen benutzt und danach wieder hergestellt.
- Einstellungen, Verlauf und Protokoll liegen unter `%APPDATA%\Zugapp`, die Sprachmodelle unter `%LOCALAPPDATA%\Zugapp\models`.

## Entwicklung

```
dotnet build src/Zugapp/Zugapp.csproj
dotnet publish src/Zugapp/Zugapp.csproj -c Release -r win-x64 --self-contained -o publish
```

Aufbau:

| Datei | Aufgabe |
|---|---|
| `TrayApp.cs` | Ablaufsteuerung: Tastenkürzel → Aufnahme → Erkennung → KI → Einfügen |
| `Core/AudioRecorder.cs` | Mikrofonaufnahme (16 kHz mono, NAudio) |
| `Core/Transcriber.cs` | Whisper-Spracherkennung und Modell-Download |
| `Core/AiPostProcessor.cs` | Überarbeitung mit Claude (Anthropic C# SDK) |
| `Core/FocusDetector.cs` | Erkennt, ob gerade ein Eingabefeld aktiv ist (Schreibmarke und UI Automation) |
| `Core/TextInserter.cs` | Einfügen per Zwischenablage + Strg+V, danach Zwischenablage wiederherstellen |
| `Ui/*` | Statusanzeige, Ergebnisfenster, Einstellungen, Verlauf, Download |
