# Biggest Boy Slots — verbindliches 5×3-Profil

Status: Alle zwölf Originalsymbole integriert; Build und automatisierte Prüfungen vorhanden. **Native Valve-/CS2-Abnahme bleibt erforderlich.**
Neue Regelversion: `anocore-biggest-boy-5x3-expanding-v4`. Neue Runden verwenden
exakt fünf Walzen mit je drei sichtbaren Symbolen, insgesamt 15 Positionen.
Das ersetzt für neue Spiele sowohl das frühere 5×5-Profil als auch den Collector-Bonus.

## Vorlage und Bedienung

Die zehn Gewinnlinien stammen direkt aus `BonusSlotRules.Paylines`. Konten,
Einsatz- und Freispielaktionen sowie Navigation verwenden die bestehende Infrastruktur.
Das am 15.10.2026 bestätigte Layout nutzt die volle Breite: vier feste schmale
Trennlinien, Bonusleiste, Einsatz links, Gewinn mittig und Drehen rechts.
Biggest Boy hat eine eigene native
XML-/CSS-Ressource. Book-XML, Book-CSS und `BonusSlotRules.cs` bleiben bytegleich;
`biggest-boy-v4-source-lock.json` und Regressionen prüfen das.

Spielaufruf: `!anocasino` → Biggest Boy. Dieselben Berechtigungen, Konten,
Einsatzschritte, Transaktionen, Operation-IDs, Restore- und Abrechnungswege bleiben
in Gebrauch. Die Darstellung mit € bezeichnet weiterhin ausschließlich virtuelle
AP-Beträge. Einsatz und mathematisches Modell werden mit der Runde eingefroren.

## Symbole und Linienauszahlungen

Die Biggest-Boy-Schärpe ist ausschließlich Wild. Döner ist das höchste reguläre
Symbol. Süßilix ist ausschließlich Freispiel-Scatter, niemals Wild. Die übrigen Bilder
sind unverändert jeweils eigenständige PNG-Dateien. Enum-IDs 0–10 bleiben wegen
alter gespeicherter Runden stabil; die Schärpe verwendet aus Kompatibilitätsgründen die interne ID `Book = 11`.

Drei, vier oder fünf passende Symbole müssen auf einer gültigen Linie von links
aufeinanderfolgen. Die Schärpe ersetzt jedes reguläre Symbol. Pro Linie wird nur
der höchste mögliche Gewinn bezahlt; mehrere gewinnende Linien addieren sich.
Die Auszahlung beträgt **Gesamteinsatz / 10 × Linienfaktor**. Es gibt keine
Scatter-Geldauszahlung und keinen zusätzlichen Expansion-Aufschlag.

| Symbol | Stopps je 100 | 3 gleiche | 4 gleiche | 5 gleiche |
| --- | ---: | ---: | ---: | ---: |
| Döner | 4 | 270 | 1350 | 3000 |
| Bier | 5 | 216 | 945 | 2900 |
| Zigarette | 6 | 162 | 675 | 2160 |
| Butter | 7 | 135 | 473 | 1485 |
| Shot | 9 | 81 | 297 | 878 |
| Trinktrichter | 10 | 68 | 216 | 675 |
| G | 14 | 26 | 68 | 194 |
| O | 14 | 26 | 68 | 194 |
| A | 14 | 13 | 60 | 129 |
| T | 14 | 13 | 60 | 129 |
| Gesicht / Scatter | 2 | – | – | – |
| Biggest-Boy-Schärpe / Wild | 1 | Ersetzt reguläre Symbole | Ersetzt reguläre Symbole | Ersetzt reguläre Symbole |

Alle fünf Walzen haben 100 feste zyklische Positionen. Scatter liegen an 0/50,
die Schärpe an 25. Die 97 regulären Positionen werden deterministisch aus den oben
aufgeführten Häufigkeiten angeordnet: Index `(n × 37 + Walze × 19) mod 97`.
Die tatsächlichen vollständigen Streifen sind reproduzierbar in den generierten
Panorama-Panels und in `BiggestBoyExpandingRules.ReelStrips` enthalten.

## Freispiele und Expansion

3/4/5 Süßilix-Symbole im Rohfeld vergeben 10/15/20 Freispiele. Beim Bonusstart wird
einmal gleichverteilt eines der zehn regulären Symbole gezogen und gespeichert;
es gibt keine zusätzliche Auswahloberfläche. Wild-Schärpe und Scatter sind ausgeschlossen.
Die Identität bleibt bis zum Bonusende unverändert.

Erscheint das ausgewählte Symbol während eines Freispiels auf einer Walze, füllt
es **genau alle drei Positionen dieser Walze**. Dieses expandierte 15er-Feld wird
anschließend einmal mit denselben zehn Linien ausgewertet. Wild-Ersetzungen und
mehrere Linien gelten auch dort. Das Rohfeld, die fünf Stopps und die expandierten
Walzen bleiben für Replay/Prüfung erhalten.

Erneut mindestens drei Scatter im **Rohfeld vor der Expansion** vergeben fünf
zusätzliche Freispiele. Die insgesamt vergebenen Freispiele bleiben höchstens 30;
dadurch werden tatsächlich höchstens 30 Freispiele gespielt. Ein Trigger im
letzten Freispiel verlängert, sofern die 30 noch nicht erreicht sind. Das Abdecken
eines Scatter-Bilds durch Expansion entfernt keinen bereits erkannten Trigger.

Nur der erste bezahlte Spin kostet Einsatz. Sämtliche Freispiele verwenden den
gesperrten Einsatz und buchen keinen weiteren ab. Die gesamte Rundenauszahlung
wird am Rundenende genau einmal gutgeschrieben. `BonusPayout` zählt separat nur
Freispielgewinne, `TotalPayout` zusätzlich den bezahlten Auslösespin. Es gibt keine
nachträgliche Kürzung: die Reservierung deckt den mathematischen Höchstbetrag
`Einsatz × 3.000 × 31` ab.

## Anzeige und Bewegung

Die bestehende Auszahlungsposition zeigt den gesamten aktuellen Spingewinn.
Unmittelbar unter den Walzen stehen Spingewinn/Freispielgewinn sowie verbleibende
Freispiele, der separate Seriengewinn und das Expanding Symbol. Die Detailposition
zeigt die Summe aller Linien und rotiert durch jede gewinnende Linie. Passende
Symbole pulsieren; die aktuell erklärte Linie wird zusätzlich markiert.

Die Ergebnisziehung und die Bewegung nutzen **denselben** festen Symbolstreifen.
Die letzte sichtbare Stoppposition wird als Start gespeichert. Die native
Animation läuft 30–129 Positionen abwärts, benötigt nun etwa 1,9–3,7 Sekunden je nach Walze
und stoppt links nach rechts im Abstand von 0,45 Sekunden. Jede 160px-Bewegung hat
eine kurze Haltephase (20 % des Schritts). Der 200ms-Vorbereitungszustand setzt
den Streifen vor Aktivierung der Animation. Das Ende ist exakt auf den gebuchten
Stop ausgerichtet; kein Austausch gegen ein unverbundenes Ergebnisband.

Vier bewegliche Bildzellen je Walze ersetzen den alten 303er-Streifen. Die Bewegung
wird serverseitig im bereits vorhandenen Präsentations-Tick entlang der gebuchten Stopps
fortgeschaltet, ohne zusätzliche Zahlungen oder clientseitigen Ergebniszufall. Für alle 10.000 Start-/Zielpaare wird die korrekte
Endposition getestet. Native FPS, Bildlauf und Parserakzeptanz sind dennoch vor
Freigabe in CS2 zu prüfen. Expansion wird erst nach dem Rohwalzenlauf sichtbar.

## Mathematische Prüfung

`python tools/audit_biggest_boy_expanding.py` berechnet die exakte Erwartung aus
allen festen Stop-Fenstern, den zehn Linien, Wild-Mehrdeutigkeiten, jedem der zehn
Expanding Symbole und der auf 30 Freispiele begrenzten Bonuskette.

- Exakte Gesamt-RTP: **94,1800002051252 %**.
- Basis-RTP: 86,54075885 %; Bonusbeitrag rund 7,63924136 %.
- Bonuschance pro bezahltem Spin: 0,19702656 %.
- Erwartete Freispiele je ausgelöster Serie: 10,25959534.
- Simulation der produktiven C#-Engine, Seed 20261015: 2.000.000 bezahlte Spins,
  4.038 Bonusserien und 41.390 tatsächlich gespielte Freispiele; RTP **94,292 %**.
- 95-%-Intervall des Simulationslaufs: 93,4160–95,1680 %. Die Stichprobe ist mit
  der exakten Erwartung vereinbar; sie ist kein Nachweis auf Nachkommastellen.

- Zweiter unabhängiger Seed 20261016: weitere 2.000.000 bezahlte Spins, RTP
  94,90108 % (95-%-Intervall 93,8842–95,9179 %). Zusammen ergeben vier Millionen
  Spins 94,59654 %; auch das kombinierte Intervall enthält den exakten Modellwert.

Rohdaten und vollständige Paytable: `biggest-boy-v4-audit.json`.
Reproduzieren:

```bash
python tools/audit_biggest_boy_expanding.py
dotnet run --project tools/AnoCore.BiggestBoyAudit -c Release -- 2000000 20261015
python tools/generate_biggest_boy_ui.py
```

## Originalbilder und native Prüfung

Alle zwölf vom Nutzer gelieferten PNGs sind bytegleich eingebunden. Die Zuordnung
und SHA-256-Prüfsummen stehen in `biggest-boy-original-assets.json`.
`süßilix.png` ist der Freispiel-Scatter (`biggest_scatter.png`).
`biggest boy.png` ist die Wild-Schärpe (`biggest_book.png`); der historische interne
Dateiname und Enumwert bleiben zur Snapshot-Kompatibilität erhalten. Es wird
kein zusätzliches goldenes Buch benötigt. Die übrigen zehn PNGs sind reguläre Symbole.
Die Originalbilder, Spielregeln und alten Quelldateien der Book-Freispiel-Slot bleiben
unverändert; Book verwendet jetzt dieselbe neue native 5×3-Oberfläche wie Biggest Boy.

Der vollständige UI-Build enthält nun alle Bilder ohne zusätzlichen Parameter:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\ui\AnoCore\build-biggest-boy.ps1 -Addon anomeme_ui -InstallLocalClient
```

Auf Windows mit installierten CS2 Workshop Tools ausführen. Das Skript kompiliert
die echten Panorama-Ressourcen; der Linux-Quellcheck ersetzt diesen Schritt nicht.
Danach im nativen Client zweimal bezahlte Spins sowie eine Bonusserie testen:
Schließen/Reconnect, Einsatzsperre, 30er-Grenze, Summen, Bildlauf, aktive Linien,
15 sichtbare Positionen, feste Trennlinien und Parserlogs prüfen. Keine
Workshop-Veröffentlichung oder Produktions-Merge vor dieser Abnahme.

## Gewinnprofil in der Konfiguration

In der bestehenden `addons/counterstrikesharp/plugins/AnoCore/config/core.json`
das Feld ergänzen (die übrigen Einstellungen behalten):

```json
{
  "BiggestBoyProfile": "standard",
  "BiggestBoyMaximumStake": 10000
}
```

Nach Änderung AnoCore kontrolliert neu laden oder den Testserver neu starten.
Das Feld wird beim Pluginstart gelesen; es gibt keine sofortige Live-Umschaltung.
Unbekannte Namen einschließlich Tippfehler werden abgewiesen.

| Profil | Stoppgewicht | Exakte Gesamt-RTP |
| --- | --- | ---: |
| `standard` | Jeder Stopp gleich wahrscheinlich | 94,1800002 % |
| `frequent` | Stopps mit G/O/A/T oben erhalten Gewicht 2 statt 1 | 92,3808189 % |
| `generous` | Fenster mit Wild oder Scatter erhalten Gewicht 11, andere 10 | 98,2685882 % |

`frequent` gewichtet kleine Symbole stärker. Mehr Treffer sind von der
Gesamtausschüttung zu unterscheiden: seine RTP liegt unter dem Standardprofil.
`generous` erhöht die langfristige Ausschüttung und Bonuswahrscheinlichkeit.
Das ist keine garantierte Gewinnquote für einzelne Sessions und keine Einstellung
für bestimmte Spieler. Physische Streifen und Auszahlungstabelle bleiben identisch;
die geprüfte Gewichtung bestimmt ausschließlich die Ziehung der fünf Stopps.

Das gewählte Profil wird in jedem akzeptierten v4-Rundenstatus gespeichert.
Fortgesetzte Freispiele verwenden nach Config-Wechsel, Reconnect oder Neustart
weiter dieses Profil. Snapshots ohne Feld `Profile` erhalten `standard`.
Einsatz, Maximalreservierung und genau einmal ausgeführte AP-Abrechnung bleiben
unverändert. Die gesamte Runde wird weiter vor der Darstellung serverseitig gebucht.

Die unabhängige exakte Rechnung akzeptiert den Profilnamen als Argument:
`python tools/audit_biggest_boy_expanding.py generous`.
Die Simulation des Produktionscodes akzeptiert ihn als drittes Argument:
`dotnet run --project tools/AnoCore.BiggestBoyAudit -c Release -- 1000000 20261017 generous`.
Prüfbericht und Trefferhäufigkeiten: `biggest-boy-profiles-audit.json`.

## Gespeicherte frühere Runden

Bereits akzeptierte v2-/v3-Runden behalten ihre historischen Regeln und maximale
Reservierung bis zum Abschluss. Sie werden nicht in v4 umgerechnet. Der alte HUD
bleibt nur für solche gespeicherten Runden zuständig; neue Biggest-Boy-Runden
verwenden ausschließlich v4 und das 5×3-HUD. Das historische v2-Profil ist separat
in `biggest-boy-legacy-v2.md` dokumentiert.

## Aktueller Prüfstand

Alle zwölf Original-PNG/VTEX-Paare bestehen den verpflichtenden CI-Assetcheck.
Automatisierte Prüfungen decken Profilvalidierung, sämtliche gewichteten Tickets,
Snapshot-Defaults, Profilwechsel bei laufenden Boni, AP-Abrechnung, zyklische
Stopps, 15 Zellen, vier feste Trennlinien und reproduzierbare UI-Generierung ab.
Native FPS, Panorama-Kompilierung und visuelle CS2-Abnahme können nur mit den
Valve-Werkzeugen und dem Client bestätigt werden.

## Native UI-Korrektur: leere Walzen (#501)

Nach dem CS2-Test mit leeren Feldern **während** der Drehung ersetzt die v4-UI
den ursprünglich 303 Zellen langen, über 48.000 px hohen Bewegungsstreifen
durch vier sichtbare Bildfelder pro Walze. Die Anzeige wird im vorhandenen
Presenter-Tick entlang derselben unveränderlichen 100er-Walzenfolgen
fortgeschaltet. Der gebuchte Stopp und die 15 Ergebnisbilder bleiben
ausschließlich serverseitig festgelegt; Darstellung, RNG, Bonus und
Auszahlung sind getrennt. Die Walzen enden nun um je 450 ms versetzt.

Profilkonto und Automatenguthaben haben eigene umrahmte Anzeigeflächen.
Unten stehen Einsatz, Rundengewinn, Automatenguthaben und Drehen nebeneinander.
Gewinnende Symbole haben einen klar roten Rand; Titel und Zahlen sind
zentriert. Der unspezifische Text »Walzen laufen« in der Gewinn-/Detailanzeige
ist entfernt. Buchungsfehler und unsichere Aktionen bleiben sichtbar.

Der Generator `tools/generate_biggest_boy_ui.py` erzeugt weiterhin reproduzierbar
XML und CSS; `tests/operator/test_biggest_boy_expanding.py` prüft die
begrenzte Geometrie, und `BiggestBoyReelMotionTests` prüft alle 10.000
Kombinationen von Start-/Zielstopps samt vier sichtbaren Bildpositionen
und Nachbarübergängen.

**Abnahme auf dem Testserver ist noch offen:** zunächst zwei bezahlte
Drehungen mit unterschiedlichen Ergebnissen prüfen, dabei auf sichtbare
Symbole während der gesamten Drehung, den vier Zeichenwechseln pro
Walzenfenster, gestaffelte Stopps und rote Gewinnrahmen achten. Danach
eine Bonusserie inklusive Kontostand, Schließen und Wiederöffnen prüfen.
Der rein textuelle Quell-/Generatorcheck beweist die native Valve-Darstellung
noch nicht. Neue nachträglich erzeugte Grafiken dürfen erst als tatsächlich
vorliegende, geprüfte PNG/VTEX-Paare gegen die älteren Quelldateien
ausgetauscht werden; keine stillen Fallbacks auf alte Symbole.

## Kürzere gestaffelte Stopps und gemeinsame Book-Oberfläche (#504)

Beide Slots nutzen nun **dieselbe** native `ano_casino_biggest.xml` und
`ano_casino_biggest.css`, die `tools/generate_biggest_boy_ui.py` erzeugt.
Der Presenter wechselt zwischen `book-mode` (Original-Früchte und BUCH-Symbol)
und `view-biggestboy` (alle zwölf unveränderten Biggest-Boy-Originalbilder).
Beim Zurückwechseln wird die Book-Texturklasse ausdrücklich entfernt.

Die Walzen stoppen bei **1,90 / 2,35 / 2,80 / 3,25 / 3,70 Sekunden**
nach Beginn der eigentlichen Animation. Die Oberflächenanimation endet bei
3,90 Sekunden; vorher sind 200 ms vorgesehen, um die Bewegung sauber
vorzubereiten. Die Stopps sind bewusst deutlich voneinander getrennt.

Bei Biggest Boy läuft das sichtbare Fenster entlang der gebuchten physischen
100er-Walzen; der tatsächliche Stopp wird aus den gespeicherten Serverdaten
bestimmt. Bei Book hatte die ursprüngliche Spielengine keine physischen
Walzenstopps, sondern bucht direkt ein 15er-Feld. Deshalb bewegt sich während
des Drehs eine rein **kosmetische, deterministische Acht-Symbol-Folge**; nach
dem Stopp wird ausschließlich das gespeicherte Book-Feld gezeigt. Keine
clientseitige Gewinnziehung und keine Änderung der Book-Auszahlungen.

Book behält die ursprünglichen Texturen Kirsche, Zitrone, Pflaume, Orange,
Traube, Glocke, Sieben und das originale BUCH-Schriftfeld. Die Symbole erhalten
bei Liniengewinnen denselben **roten Rand**. Für Book sind auch die
Freispielanzahl, das Bonus-/Expanding-Symbol, der aktuelle Freispielgewinn,
die kumulierte Auszahlung und ein eigener passender Gewinnplan sichtbar.
Die alten Dateien `ano_casino_rounds.xml`, `ano_casino_rounds.css`
und `BonusSlotRules.cs` bleiben bytegleich; sie werden für Blackjack,
Risk und fortgesetzte ältere Biggest-Boy-Runden gebraucht.

### Nativer Test

1. Den neuesten AnoCore-Testbuild mit PR #505 auf den Testserver laden.
2. Unter Windows im **Wurzelordner dieses Builds** PowerShell starten und
   die unveränderten Original-PNGs mit `ui/AnoCore/build-biggest-boy.ps1`
   für `anomeme_ui` kompilieren (`-InstallLocalClient` für den lokalen Test).
3. Im Spiel `!anocasino` aufrufen; Biggest Boy und Book separat
   zweimal drehen, alle fünf Stopps beobachten, rote Rahmen und
   Freispiele samt Geldwerten kontrollieren.
4. Im laufenden Menü zwischen Biggest Boy und Book wechseln und prüfen,
   dass niemals Fruchtbilder in Biggest Boy oder Biggest-Boy-Bilder in
   Book erscheinen.

Der native CS2-/Steam-Workshop-Test steht weiterhin aus.

## Unabhängige Ergebnisregler (#509)

Neue Plugin-Runden verwenden `anocore-biggest-boy-controlled-v5` bzw.
`anocore-book-controlled-v2`. Bestehende v1/v2/v3/v4-Runden behalten ihre
bisherigen Regeln. Gesicht bleibt Scatter, Biggest-Boy-Schärpe bleibt Wild.
Das transparente Kokosnuss-PNG ersetzt das frühere Frucht-Buch-Bild bytegenau.

In `config/core.json` die vorhandenen Felder behalten und ergänzen:

```json
{
  "BiggestBoyOutcomes": { "WinPercent": 25, "FreeSpinPercent": 1, "FullScreenPercent": 1 },
  "BookOutcomes": { "WinPercent": 25, "FreeSpinPercent": 1, "FullScreenPercent": 1 },
  "CasinoReelStopSoundEnabled": true
}
```

Alle Werte sind ganzzahlige Prozente von 0 bis 100. `WinPercent` ist die
Wahrscheinlichkeit eines tatsächlichen Nettogewinns (Auszahlung größer als
Einsatz); bei Freispielen bedeutet es Auszahlung größer als null.
`FreeSpinPercent` zieht unabhängig davon einen Freispieltrigger. Ein Spin kann
also gewinnen, Freispiele auslösen, beides oder keines. 0 unterbindet das
jeweilige Ereignis, 100 garantiert es, solange bei Verlängerungen noch Platz
unter der unveränderten 30-Freispiel-Grenze besteht. Gewinne und Auszahlungen
werden aus dem angezeigten Feld mit den bisherigen Linien und Faktoren berechnet.
`FullScreenPercent` ist die zusätzliche Vollbildchance innerhalb gewinnender
Spins ohne Scattertrigger; alle regulären Symbole sind dafür möglich.
Natürlich gezogene Vollbilder können zusätzlich auftreten. Ein Vollbild und drei
Scatter können nicht gleichzeitig dasselbe 15er-Feld belegen.

Die Zufallsauswahl wird serverseitig vor der Darstellung gebucht. Neue Runden
verwenden virtuelle Symbolfelder statt der historischen festen 100er-Streifen,
weil diese keine frei einstellbaren, unabhängigen Ergebnisse oder Vollbilder
aller Symbole darstellen können. Es gibt höchstens 128 Feldversuche; ein
endlicher Konstruktionsweg sichert seltene Kombinationen ab. Wilds werden als
Kandidaten mit 2 % pro Zelle gezogen, anschließend auf das vorab gewählte
Gewinnereignis konditioniert. Daher sind 2 % keine garantierte sichtbare
Wildhäufigkeit. Einzelne/zweifache Scatter können auch ohne Bonus erscheinen.
3/4/5 Gesichter vergeben weiter 10/15/20 Freispiele; ein Retrigger gibt +5.

Die neuen Regler ersetzen bei neuen Plugin-Runden `BiggestBoyLuckPercent`
und `BiggestBoyProfile`. Diese bleiben für ältere direkte API-Aufrufer und
akzeptierte Altrunden erhalten. Die alten RTP-Werte gelten ausschließlich für
die alten Profile, nicht für diese frei konfigurierbaren Ergebnisverteilungen.
Die Gewinnchance ist keine RTP-Einstellung: hohe Gewinn- oder Vollbildwerte
können weit mehr als den Einsatz ausschütten.

Alle drei Einstellungen werden mit der Runde gespeichert. Konfigurationsänderungen
wirken nach kontrolliertem Neuladen nur auf neue Runden. Der Einsatz bleibt während
der gesamten Freispielserie gesperrt und wird nach deren Ende wieder bedienbar.
Die bestehende Reservierung deckt weiterhin sämtliche möglichen Linienauszahlungen.
`BonusSlotRules.cs` erhielt ausschließlich ein optionales Snapshot-Metadatenfeld;
seine bisherigen Ziehungs- und Auswertungsalgorithmen bleiben unverändert.

Im gemeinsamen Slotfenster schaltet `WALZENTON: AN/AUS` die Stopptöne für die
aktuelle Spielersitzung. Der globale Schalter kann sie vollständig abschalten.
Die vorhandene CS2-Datei wird über `EmitSound("UI.CounterBeep", ..., volume: 0.12f)` leise nur beim jeweiligen
Spieler abgespielt; fünf gestaffelte Stopptöne pro Drehung, keine Sounddatei im
Workshop nötig. Native Tonwiedergabe und Panorama-Anordnung sind auf CS2 zu prüfen.

## Gemischte kosmetische Walzenfolgen (#512)

Die v5-Oberfläche wiederholt während des Drehens nicht mehr die drei bereits
gebuchten Ergebnisbilder. Jede neue Darstellung erhält je Walze eine eigene,
zufällig gemischte Folge aller zwölf Biggest-Boy-Symbole.
Gemischte Symbolbeutel halten sämtliche Motive in der Animation sichtbar.
Beide neuen Slot-Darstellungen wechseln nun mit etwa zehn Symbolschritten pro
Sekunde statt bisher etwa 18. Die Folge bleibt während einer Drehung unverändert; beim Abwärtslauf wandern
Nachbarbilder zusammen weiter, statt in jedem Frame unabhängig ausgetauscht
zu werden. Erst in den letzten drei langsameren Schritten fahren die gebuchten
Bilder ins Walzenfenster. Die Stopps bleiben bei 1,90 / 2,35 / 2,80 / 3,25 /
3,70 Sekunden nach der 200-ms-Vorbereitung.

Die kosmetische Zufallsauswahl verwendet ausschließlich Präsentationszustand.
Sie ruft keine Spielregelziehung auf, ändert weder gespeichertes Roh-/Ergebnisfeld
noch Gewinnchance, Freispiele, Einsatz oder Kontobuchungen. Historische v4-Runden
mit physischen Streifen behalten ihre bisherige Walzenbewegung. Die bereits funktionierende separate Acht-Symbol-Folge von Book bleibt
erhalten; lediglich ihr Bildwechsel wird auf zehn Schritte pro Sekunde reduziert. Sound-Schalter, fünf Stopptöne, rote Gewinnrahmen und
die Einsatzsperre während einer Bonusserie bleiben erhalten.

Diese Korrektur erfordert nur das passende Plugin-Update. XML, CSS und Bilder
sind zum Paket 509 unverändert; kein erneutes Kompilieren oder Veröffentlichen
des Workshop-Addons erforderlich. Native Abnahme: beide Slots je zweimal drehen,
wechselnde Motive und kontinuierliche Nachbarn beobachten, fünf getrennte Stopps
sowie die exakte Ergebnisanzeige und eine Freispielserie kontrollieren.
