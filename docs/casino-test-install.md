# AnoCasino schnell installieren und testen

Diese Anleitung richtet einen Testserver und einen Windows-Client ein. Der Server benötigt das passende AnoCore-Plugin, der Client die kompilierten Panorama-Ressourcen. Für einen Test mit mehreren Spielern muss das UI als Workshop-Addon veröffentlicht und vom Server an die Clients ausgeliefert werden.

## 1. Server-Plugin aktualisieren

1. Entpacke das passende `AnoCore-Casino-Test`-Paket beziehungsweise das Artefakt `AnoCore-development` des zugehörigen erfolgreichen CI-Laufs. Ein lokales Testpaket ohne abgeschlossene CI ist als solches gekennzeichnet.
2. Stoppe den Server und sichere den bestehenden Ordner `game/csgo/addons/counterstrikesharp/plugins/AnoCore`.
3. Ersetze dort die veröffentlichten Programmdateien mit dem Inhalt aus `plugins/AnoCore` des Artefakts. Kopiere nicht nur `AnoCore.dll` und erzeuge keinen verschachtelten Ordner `AnoCore/AnoCore`.
4. Behalte den vorhandenen Ordner `config` und insbesondere `config/core.json` samt Datenbankverbindung bei. Überschreibe beim Update keine Konfigurationsdateien. CounterStrikeSharp und MultiAddonManager werden nicht mit diesem Paket aktualisiert.

Ergänze in der bestehenden `config/core.json` die beiden Schalter und lass alle übrigen Einstellungen unverändert:

```json
"PanoramaMenusEnabled": true,
"CasinoEnabled": true
```

`CasinoEnabled` setzt `PanoramaMenusEnabled` voraus. Beim nächsten Serverstart wendet AnoCore die ausstehenden Casino-Schemamigrationen 027 und 028 automatisch an. Vor dem ersten Start mit dem Update sollte ein Datenbankbackup vorliegen. Migrationen müssen nicht manuell gestartet werden.

## 2. UI bauen und an die Clients bringen

Auf einem Windows-PC mit installiertem CS2 und CS2 Workshop Tools öffne PowerShell im entpackten Paketordner mit dem Unterordner `ui`. Der Standard-Build aktualisiert das bestehende kombinierte Addon `anomeme_ui` und erhält vorhandene Map-Vorschau-Zuordnungen:

```powershell
powershell -ExecutionPolicy Bypass -File .\ui\AnoCore\build.ps1 -Addon anomeme_ui
```

Falls CS2 nicht unter einem der automatisch gefundenen Steam-Pfade liegt, gib den Installationspfad explizit an:

```powershell
powershell -ExecutionPolicy Bypass -File .\ui\AnoCore\build.ps1 -Cs2 "D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive" -Addon anomeme_ui
```

Der Build legt Quellen unter `content/csgo_addons/anomeme_ui/panorama/...` ab und kompiliert die Ressourcen nach `game/csgo_addons/anomeme_ui/panorama/...`. Er veröffentlicht nichts automatisch. Öffne anschließend die vorhandene `anomeme_ui`-Quelle in Workshop Manager und aktualisiere den bestehenden Workshop-Eintrag mit **Re-Upload → anomeme_ui → Submit**. Verwende die Workshop-ID des eigenen Addons. Auf dem Server müssen die bestehende Addon-Auslieferung und deren IDs erhalten bleiben; bei der ANOMEME-Installation ist `anomeme_ui` bereits über MultiAddonManager konfiguriert. Andere Server nutzen ihre eigenen IDs und Einstellungen.

Ein separates Casino-Addon ist ebenfalls möglich, aber nur als Alternative: `build-casino.ps1 -Cs2 "<CS2-Pfad>"` baut das Addon `anocasino_ui`. Dieses muss neben `anomeme_ui` veröffentlicht und gemountet werden. Die `ano_arcade`-Ressourcen dürfen nicht aus beiden Addons gleichzeitig gemountet sein. Für den schnellen Test daher beim kombinierten `anomeme_ui` bleiben.

### Einzelclient-Smoke-Test ohne Workshop-Upload

Für einen lokalen visuellen Test auf genau dem Windows-PC kannst du die kompilierten Dateien zusätzlich direkt in den lokalen CS2-Client kopieren lassen:

```powershell
powershell -ExecutionPolicy Bypass -File .\ui\AnoCore\build.ps1 -Cs2 "D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive" -Addon anomeme_ui -InstallLocalClient
```

`-InstallLocalClient` kopiert `.vxml_c`, `.vcss_c` und Symbol-/Banner-Texturen nach `game/csgo/panorama/...`. Das ist nur ein Client-Smoke-Test; es aktualisiert weder Workshop noch andere Spieler-Clients. Server-Plugin und Workshop-Delivery müssen für den eigentlichen Online-Test weiterhin eingerichtet sein.

## 3. Testkonto berechtigen und aufladen

Starte oder starte den Server nach Plugin- und Konfigurationsupdate neu. In der Serverkonsole (nicht im Spielerchat) führe die Befehle einzeln aus. Ersetze `<SteamID64>` durch die SteamID64 des Testspielers; verwende hier ausdrücklich keinen geratenen Beispielwert:

```text
css_anoroledefine casino_player 0 ano.casino.play
css_anograntrole <SteamID64> casino_player 0 Casino-Test
css_anoapcredit <SteamID64> 5000
```

Prüfe vor dem Definieren, ob eine Rolle `casino_player` bereits existiert. `css_anoapcredit` ist nur aus der Serverkonsole verfügbar und schreibt eine dauerhafte virtuelle AP-Gutschrift. Die Rolle braucht `ano.casino.play`; CSS-Adminrechte allein ersetzen diese Berechtigung nicht. Wiederhole Grant und Gutschrift mit der tatsächlichen SteamID64 für jeden weiteren Tester.

## 4. Im Spiel testen

Lass den Testspieler dem Server beitreten und gib im Chat `!anocasino` ein. Alternativ öffne `!anomenu` → **Open menu** → **AnoCasino**. Der Spieler sollte Profilkonto und Automatenguthaben sehen. Lade AP vom Profilkonto in den Automaten, wähle einen Einsatz und nutze die Spielauswahl oben in der Lobby. Prüfe dort die sechs Spielkarten und öffne nacheinander **Slots**, **Coinflip**, **Roulette**, **Blackjack**, **Risk** und **Book**. Spiele jeweils eine Runde; kontrolliere Einsatz, Ergebnis, Kontostand und Verlauf. Die Animation darf das serverseitig gebuchte Ergebnis nur darstellen, nicht verändern.

Wenn eine Karte fehlt oder sich nicht öffnen lässt, notiere Plugin-/UI-Buildrevision, Serverlog und den genauen Schritt. Ein erfolgreicher Build oder vorhandene Engine-Tests belegen noch keine native Presenter-, Panorama- oder Workshop-Abnahme. Insbesondere müssen die drei erweiterten Spiele und die tatsächliche Darstellung auf dem CS2-Testserver bestätigt werden.

Führe mindestens diese kurzen Checks aus: Guthaben laden und zurückbuchen, mit zu wenig AP spielen, Mehrfachklick während Animation, Fenster schließen und wieder öffnen, Berechtigung entziehen, reconnecten sowie den Server neu starten. `!anoap` zeigt das Profilguthaben. Die Lobby-Verlaufsliste enthält die bisherigen Einzelspiele; Blackjack, Risk und Book führen ein separates dauerhaftes Rundenledger. Vergleiche Konto und aktive Runde nach Neustart mit dem Stand davor; AP dürfen nur durch dokumentierte Einsätze/Auszahlungen oder die Konsolengutschrift verändert werden.

## Grenze des Tests

Alle AP sind virtuelle Testpunkte ohne Echtgeld-Ein- oder Auszahlung. Native Darstellung, Workshop-Auslieferung und das Verhalten mit zwei Clients brauchen echte CS2-Abnahme. Quellcodeprüfung und CI ersetzen diesen Test nicht.
