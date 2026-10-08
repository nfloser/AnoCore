# Schnellinstallation: Windows und DatHost

Für Serverbetreiber. Das Serverplugin und das Workshop-Addon sind zwei getrennte Updates.
Verwende einen erfolgreichen CI-Build, der die gewünschte Dashboard-Version enthält.
`BUILD-COMMIT.txt` dokumentiert den Stand. Ein älterer Build füllt neue Dashboard-Panels nicht.
Es ist kein Battlepass enthalten.

## Voraussetzungen

CS2-Server mit Metamod, CounterStrikeSharp API 374 oder neuer und kompatiblem .NET-10-Host;
eine eigene MySQL/MariaDB-Datenbank. Für den UI-Build: Windows, CS2 und CS2 Workshop Tools.
Siehe [Server- und Datenbanksetup](server-setup.de.md) sowie [Deployment](deployment.md) für Voraussetzungen und Rollback.

## Serverplugin installieren oder aktualisieren

1. In GitHub Actions den erfolgreichen passenden CI-Lauf öffnen, `AnoCore-development` herunterladen und entpacken.
2. DatHost-Server stoppen und den bisherigen AnoCore-Ordner einschließlich Konfiguration sichern.
3. **Quelle auf dem PC:** Inhalt von `plugins\AnoCore` im entpackten Paket.
4. **Ziel auf DatHost:** `game/csgo/addons/counterstrikesharp/plugins/AnoCore`.
   Beginnt der Dateimanager bereits bei `game`, ist der sichtbare Pfad `csgo/addons/...`.
5. Alle veröffentlichten Programmdateien ersetzen. Keinen zusätzlichen `AnoCore/AnoCore`-Ordner erzeugen.
   **Bestehenden `config`-Ordner behalten**, insbesondere `core.json` und den Connection String.
   Falls das Paket Konfigurationsdateien enthält, diese beim Update ausschließen.
6. Bei Erstinstallation erzeugt der erste Start `config/core.json`. Server anschließend stoppen,
   den eigenen Connection String eintragen und erneut starten. Keine Zugangsdaten ins Repository schreiben.
7. Für die native UI in der bestehenden `config/core.json` `"PanoramaMenusEnabled": true` setzen.
8. Nicht CounterStrikeSharp oder MultiAddonManager durch Dateien dieses Pakets ersetzen.

Nur `AnoCore.dll` zu kopieren reicht nicht: Alle veröffentlichten Abhängigkeiten gehören zusammen.

## Workshop-Addon installieren oder aktualisieren

Die vollständigen Schritte stehen in [Steam-Addon und Logo](steam-addon.de.md).
Vom Repository- oder Paketordner mit `ui` aus in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\ui\AnoCore\build.ps1 -Addon anomeme_ui
```

Nach erfolgreicher Kompilierung das bestehende Addon im Workshop Manager per
**Re-Upload → anomeme_ui → Submit** aktualisieren. Für die ANOMEME-Installation
bleibt die Workshop-ID `3815363712` erhalten. Andere Betreiber verwenden ihre eigene ID.
Die bestehenden MultiAddonManager-Einstellungen kontrollieren:

```text
mm_extra_addons "3815363712"
mm_addon_mount_download "1"
```

Weitere eingetragene Addon-IDs beibehalten. Diese Werte gehören zur Server-/MultiAddonManager-Konfiguration,
nicht in AnoCores `core.json`. Die genaue CFG-Datei hängt vom vorhandenen Server-Setup ab.

## Start und Kontrolle

Upload/Freigabe abwarten, Server starten und CS2 vollständig neu starten.
Serverkonsole: `css_anostatus`. Spielchat: `!anostatus`, danach `!anomenu`.
Dashboard, Logo, Challenge-Vorschauen, Open menu, Back, Home, Seitenwechsel und Close prüfen.
Back ist nur auf dem Dashboard deaktiviert; Prev page auf Seite 1 ist ebenfalls deaktiviert.
Mit zwei Spielern prüfen, dass jeder seine eigenen Werte sieht.

Bereits aktualisiertes Workshop-Addon? Dann nur das passende Serverplugin aktualisieren.
Nur CSS/XML/Logo geändert? UI neu bauen und hochladen; kein C#-Build nötig.
Für Rückkehr zum alten Stand Server stoppen und die gesicherten Programmdateien sowie die passende UI-Version verwenden.
Datenbankschema-Rollback gesondert nach [Deployment](deployment.md) behandeln.
