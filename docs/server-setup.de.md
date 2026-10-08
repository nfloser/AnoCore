# Server und Datenbank einrichten

Diese Anleitung ergänzt die [Schnellinstallation](quick-install.de.md).
Sie beschreibt eine neue Installation und Updates auf einem vorhandenen DatHost-Server.

## 1. Server vorbereiten

CS2-Server anlegen, Metamod und CounterStrikeSharp installieren beziehungsweise deren vorhandene Installation prüfen.
AnoCore benötigt CounterStrikeSharp API 374 oder neuer und einen kompatiblen .NET-10-Host.
Das Pluginpaket bringt CounterStrikeSharp und MultiAddonManager nicht mit.
Verwende zu deinem CS2-Stand kompatible Builds dieser Komponenten.
Vor Updates Server stoppen und Pluginordner sowie Datenbank sichern.

DatHost stellt Datei- und Konsolenzugriff bereit. Beginnt der Dateimanager bereits bei `game`,
diesen Präfix bei allen folgenden Pfaden weglassen.

## 2. Datenbank vorbereiten

AnoCore verwendet MySQL/MariaDB. Lege eine eigene Datenbank und einen eigenen Benutzer an;
verwende nicht den Root-Account im Plugin. Bei einem verwalteten Datenbankangebot dessen
Host, Port, Datenbankname, Benutzer und Passwort übernehmen. `localhost` ist nur richtig,
wenn die Datenbank vom CS2-Prozess aus tatsächlich lokal erreichbar ist.

Bei selbst verwalteter MariaDB kann der Datenbankadministrator beispielsweise ausführen:

```sql
CREATE DATABASE anocore CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'anocore'@'CS2_SERVER_HOST' IDENTIFIED BY 'REPLACE_WITH_STRONG_PASSWORD';
GRANT ALL PRIVILEGES ON anocore.* TO 'anocore'@'CS2_SERVER_HOST';
```

`CS2_SERVER_HOST` durch den Host ersetzen, von dem die Datenbank die Serververbindung sieht.
Die Rechte sind auf die eigene Datenbank beschränkt; `ALL PRIVILEGES` umfasst hier auch die
Schemaänderungen, die die aktuellen Startmigrationen benötigen. Nicht pauschal auf `*.*` freigeben.
Bei Managed Hosting kann diese Einrichtung über dessen Oberfläche erfolgen.
Die Datenbank nur für den erforderlichen Server erreichbar machen und TLS nach den Vorgaben
des Datenbankanbieters konfigurieren. Zugangsdaten nicht ins Repository, Chat oder Logs kopieren.

## 3. Plugin hochladen und Verbindung konfigurieren

1. Passenden erfolgreichen CI-Build herunterladen und entpacken.
2. Server stoppen. Alle Dateien aus lokal `plugins/AnoCore` nach
   `game/csgo/addons/counterstrikesharp/plugins/AnoCore` hochladen.
3. Bei einer neuen Installation einmal starten, damit die Vorlage
   `plugins/AnoCore/config/core.json` erzeugt wird, danach wieder stoppen.
4. In dieser Datei den bestehenden Wert `ConnectionString` ergänzen:

```json
{
  "ConnectionString": "Server=DB_HOST;Port=3306;Database=anocore;User ID=anocore;Password=YOUR_PASSWORD;Connection Timeout=10;Default Command Timeout=15",
  "PanoramaMenusEnabled": false
}
```

Dies ist ein Ausschnitt für die Einrichtung, **kein Ersatz für eine vorhandene vollständige core.json**.
Alle anderen erzeugten Eigenschaften behalten. Alternativ kann `ANOCORE_MYSQL` in der
Serverumgebung gesetzt werden; diese Variable hat Vorrang vor der Datei.

5. Server starten und Konsole auf Datenbank-/Loaderfehler prüfen. AnoCore führt seine Schema-Migrationen beim Start aus.
6. `css_anostatus` in der Serverkonsole ausführen. `ready` bestätigt den Start der gemeinsamen Dienste;
   es bestätigt nicht die vollständige fachliche Prüfung jedes Moduls.

Bei `not configured` den Connection String prüfen; bei `startup failed` den konkreten
Startfehler prüfen und nach der Korrektur neu starten. Bei Verbindungsfehlern Host, Port,
Netzwerkerreichbarkeit, Benutzer-Hostbindung und Passwort kontrollieren. Keine Tabellen manuell
löschen, um einen Fehler zu übergehen. Details und Rollback: [Deployment](deployment.md).

## 4. Addon und MultiAddonManager

Das Workshop-Addon enthält kompiliertes Panorama-Layout, CSS und Logo. Es ersetzt nicht das Serverplugin.
MultiAddonManager ist die separate Serverkomponente für zusätzliche Workshop-Addons;
seine Installation erfolgt nach den Anweisungen des verwendeten Builds.
[Upstream-Repository](https://github.com/Source2ZE/MultiAddonManager).

Im bestehenden MultiAddonManager-/Server-CFG-Setup werden für ANOMEME verwendet:

```text
mm_extra_addons "3815363712"
mm_addon_mount_download "1"
```

Die erste Einstellung referenziert den bestehenden UI-Workshop-Eintrag.
Die zweite bleibt im verwendeten Setup für das Herunterladen beim Mounten aktiviert.
Weitere Addon-IDs erhalten. Verwende auf einem eigenen Workshop-Eintrag dessen tatsächliche ID.
Die konkrete CFG-Datei hängt von der Installation ab: in den vorhandenen Server-CFGs nach
`mm_extra_addons` und `mm_addon_mount_download` suchen. Keine beliebige zweite Konfigurationsdatei
anlegen, deren Laden nicht gesichert ist. Beide Werte gehören **nicht** in AnoCores `core.json`.

Zuerst das Addon [auf Windows bauen und hochladen](steam-addon.de.md), dann prüfen,
dass Server und ein sauberer Client es herunterladen/mounten. Erst danach in der bestehenden
`core.json` `"PanoramaMenusEnabled": true` setzen und den Server neu starten.
Teamnamen, Stats und Challenge-Werte liefert das Plugin; Änderungen dieser Daten brauchen
keinen neuen Workshop-Upload. Layout-, CSS- und Bildänderungen dagegen schon.

## 5. Einstellungen, Rechte und Turniere

- Persönliche Optionen: `!anosettingsmenu`; die angezeigten Optionen hängen von geladenen Modulen ab.
- Serverkonfiguration: `plugins/AnoCore/config`; erzeugte Dateien bearbeiten und den dokumentierten
  [Reload](configuration-reload.md) beziehungsweise erforderlichen Neustart verwenden.
- Rechte: [Rollenverwaltung](authorization.md); nicht jeder Spieler bekommt Adminrechte.
- Turnier: `!anotournamentload` erzeugt bei fehlender Konfiguration eine deaktivierte
  `tournament-match`-Vorlage. Teamnamen, Captains und SteamID64-Mitglieder eintragen,
  `Enabled` aktivieren und erneut laden. Vollständige Felder und Phasen stehen in [Turniere](tournament.md).
  Keine zusätzliche frei erfundene Teams-Datei nötig.

## 6. Abnahme und Updates

Server und CS2 neu starten. `!anostatus`, `!anomenu`, Statistiken, Einstellungen und Veto prüfen.
Mit zwei Spielern getrennte Daten, Mausfreigabe, Reconnect und Mapwechsel testen.
DB-Persistenz nach Serverneustart kontrollieren. [Vollständiger Testplan](full-system-test.md).

Bei Pluginupdates den gesamten veröffentlichten Binärsatz ersetzen und **config behalten**.
Datenbank vorher sichern. Bei UI-Updates bauen, denselben Workshop-Eintrag aktualisieren und
Client/Server neu starten. Bereits aktualisierte UI muss nicht für ein reines Pluginupdate erneut hochgeladen werden.
