# Arcade meldet „geöffnet“, aber kein Fenster erscheint

`Unable to find panel with id 'ano_arcade_account'` (und die weiteren
Arcade-IDs) bedeutet: Der Client kann die erwarteten Panels nicht auflösen.
Die passende Quell-XML enthält diese IDs. Ein serverseitiger „HUD ready“-Log
belegt nur die erstellte Entity, nicht die geladene Clientdatei. Das gemountete
Workshop-Addon allein beweist ebenso wenig, dass dessen aktuelle Datei greift.
Die beobachteten Meldungen passen zu fehlenden/veralteten oder überschriebenen
Clientressourcen; die konkrete native Ursache ist damit noch nicht bewiesen.

## Gezielter Test ohne neues Server-Plugin

1. CS2 vollständig schließen. Die bereits laufende Serverversion mit aktiviertem
   `ArcadeAnimationProbeEnabled` und `ano.casino.test` kann unverändert bleiben.
2. Das Arcade-Reparaturpaket entpacken. PowerShell im Ordner öffnen, der `ui`
   enthält, und ausführen:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\ui\AnoCore\repair-arcade.ps1
   ```

   Falls CS2 nicht automatisch gefunden wird:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\ui\AnoCore\repair-arcade.ps1 -Cs2 "D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"
   ```

3. CS2 starten, dem Testserver beitreten und `!anoarcadetest` eingeben.
   Erwartet: ein schwarz-grünes Fenster mit der festen Kennung
   **ARCADE UI CHECK 2026-10-14**. Die Kennung benötigt keine Server-Textbindung.
   Slots auswählen, Test-AP aufladen und drehen. Fenster schließen und erneut
   öffnen. Auch beim zweiten Aufruf müssen die Panels gefunden werden.

Der Test ersetzt genau fünf lose Clientdateien: Arcade-Layout, Arcade-Stylesheet
und drei Fruchtsymbole. Vorhandene Dateien bleiben im ausgegebenen Ordner
`anocore-ui-backups` gesichert. Das Menü und die Veto-/Turnierressourcen werden
nicht verändert. Der Build prüft alle erwarteten IDs, verwirft alte kompilierte
Zieldateien vor dem Compilerlauf und installiert erst nach fünf erfolgreichen
neuen Ausgaben. Ein Manifest dokumentiert deren SHA256-Werte.

## Danach über Workshop testen

Mit `-Mode BuildOnly` dieselben Dateien in das bestehende `anomeme_ui` bauen und
dessen vorhandenen Workshop-Eintrag aktualisieren. Danach bei geschlossenem CS2
die fünf losen Testdateien sichern und aus dem direkten Clientpfad entfernen:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\ui\AnoCore\repair-arcade.ps1 -Mode UseWorkshop
```

Damit verdecken lokale Testdateien die Workshop-Version nicht mehr. Erst dann
nach Client-/Server-Neustart die gleiche sichtbare Kennung auch über Workshop
und mit einem zweiten Client bestätigen. Der lokale Test veröffentlicht nichts.
Der Server muss das bestehende Addon weiterhin ausliefern.

Bleibt das Fenster trotz erfolgreich geprüftem lokalem Build leer, komplette
Clientkonsole ab dem Beitritt erfassen, insbesondere XML-/CSS-Ladefehler. Ein
zweiter Aufruf einige Sekunden später trennt ein mögliches Lade-Timingproblem
von einem dauerhaft fehlenden/falschen Layout. Keine weiteren Rollen/AP-Befehle
sind nötig, wenn der alte Aufruf bereits die Öffnung bestätigt hat.

PowerShell-Integrationstests mit einem Fake-Compiler prüfen frische Ausgaben,
Fehler vor Clientänderungen, Sicherungen und die enge Workshop-Bereinigung.
Sie ersetzen die echte Valve-Kompilierung und die sichtbare CS2-Abnahme nicht.
