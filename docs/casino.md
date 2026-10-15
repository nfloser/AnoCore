# AnoCasino – spielbare Version und Bildertausch

> Erweiterungsstatus: Blackjack, Risk und der Book-artige Bonus-Slot werden in
> separaten Regel- und Persistenzpaketen entwickelt. Sie sind erst nach nativer
> Presenter-/Plugin-Integration spielbar. Kistenöffnung und simuliertes Inventar
> gehören ausdrücklich nicht zu AnoCasino.

Diese Version ergänzt den separaten Drei-Frucht-Animationstest. Der bestehende Stand
`anocasino-v1` bleibt der Legacy-Vertrag für gespeicherte Vorgänge; neue
Spiel-Engines dürfen dessen Auswertungen und Belege nicht verändern. Slots, Coinflip
und Roulette sowie Blackjack, Risk und Book-Freispiele sind über die native Lobby angebunden. Die native CS2-Darstellung und alle
Workshop-Ressourcen müssen auf dem Testserver abgenommen werden; Sourcechecks
und CI ersetzen diese Abnahme nicht. Keine Echtgeld-Einzahlung oder Auszahlung.

## Aktivierung

Passendes Plugin und aktualisiertes Workshop-Addon zusammen installieren. In der
bestehenden `config/core.json` die übrigen Werte erhalten und ergänzen:

```json
"PanoramaMenusEnabled": true,
"CasinoEnabled": true,
"ArcadeAnimationProbeEnabled": false
```

Server neu starten. Schema-Migrationen 27/28 erzeugen AP-Konten, Vorgangsledger und dauerhafte Runden;
sie wird vor Aktivierung des Casinos angewendet. Das gemeinsame Konto hängt an
der SteamID64, nicht am Namen, Slot oder an einer CS2-Sitzung. Es ist unabhängig
von XP und Rangpunkten. Ein neues Profil startet mit null AP.

In der Serverkonsole einzeln:

```text
css_anoroledefine casino_player 0 ano.casino.play
css_anograntrole <SteamID64> casino_player 0 Casino-Test
css_anoapcredit <SteamID64> 5000
```

`anoapcredit` ist ausschließlich aus der Serverkonsole erlaubt, nimmt 1 bis
1.000.000 AP an und schreibt eine dauerhafte Ledgerbuchung. Keine automatische
Punktevergabe beim Login. Für andere Spieler nur den Grant wiederholen. Der
Befehl ist unabhängig von CSS admins.json. Vorhandene Rollen vor Neudefinition
prüfen. Das Casino-Konto ist auf insgesamt 1.000.000.000 AP begrenzt. Vor einem Spiel muss genug
Spielraum für die maximal mögliche Auszahlung bestehen; dies wird vor der
Zufallsziehung geprüft. Gewinnauszahlungen werden nicht nachträglich gekappt.

Im Chat `!anocasino` oder `!anomenu` → Open menu → AnoCasino.
`!anoap` zeigt Profilguthaben und reserviertes Automatenguthaben. Das Dashboard
übernimmt diese Anzeige über die registrierte Kontoabfrage.

## Bedienung

- Aufladebetrag mit ± wählen (50–10.000 AP), dann **Aufladen**. Dies ist eine
  Umbuchung vom Profilkonto, keine neue Punktevergabe.
- **Alles zurückbuchen** verschiebt das gesamte Automatenguthaben ins Profil.
- Einsatz mit ± wählen (50–500 AP, Schritte von 50).
- Spiel und Auswahl wählen, dann **Spielen**. Während einer Buchung/Animation
  werden neue Wetten, Umbuchungen und Auswahlwechsel serverseitig verworfen.
- **Verlauf** zeigt die letzten fünf Spiele aus den letzten zwanzig Vorgängen.
- **AnoMenu** schließt das Casino und öffnet das Dashboard. Schließen und
  Überspringen ändern bereits gebuchte Ergebnisse nicht. Nach Reconnect,
  Mapwechsel oder Neustart bleiben Konto, Reservierung und Ledger erhalten.
- **Buchung prüfen** wiederholt bei unbestätigtem Ergebnis dieselbe Vorgangs-ID.
  Kein neues Ergebnis und keine doppelte Abbuchung. Bis zur Klärung sind neue
  Buchungen gesperrt. Nach einem kompletten Client-/Pluginneustart Konto und
  letzten Spielverlauf prüfen, bevor eine neue Wette abgegeben wird.

## Versionierte Spielregeln: anocasino-v1

Die Regeln sind eigene Regeln, keine mathematische Kopie eines kommerziellen
Automaten. Alle genannten Auszahlungen enthalten den zurückgegebenen Einsatz.

### Slots

Drei Walzen, drei sichtbare Reihen, fünf Linien: drei horizontale Reihen und die
beiden Diagonalen. Der Gesamteinsatz wird gleichmäßig auf die fünf Linien
verteilt. Drei gleiche Symbole auf einer Linie gewinnen. Jede der neun Zellen
wird unabhängig mit den folgenden Gewichten gewählt:

| Symbol-ID | Gewicht von 112 | Auszahlung pro Linien-Einsatz |
|---|---:|---:|
| cherry | 30 | 12× |
| lemon | 25 | 22× |
| plum | 20 | 36× |
| orange | 15 | 55× |
| grapes | 10 | 100× |
| watermelon | 7 | 150× |
| bell | 4 | 300× |
| seven | 1 | 1.000× |

Mehrere Linien addieren sich. Kein Wild, Scatter, Jackpot, Freispiele oder
Autospin in diesem Regelstand. Die theoretische langfristige Rückzahlung ist
93,4585259885 %: Summe `(Gewicht / 112)^3 × Multiplikator`. Kein Versprechen für
eine einzelne Sitzung. Walzenanimation zeigt das bereits gebuchte Ergebnis.

### Coinflip

Kopf oder Zahl auswählen. Zwei gleich wahrscheinliche Ergebnisse aus
serverseitiger kryptografischer Zufallsquelle. Richtige Auswahl: 2× Gesamteinsatz;
falsche Auswahl: null. Theoretische Rückzahlung 100 %.

### Roulette

Europäisches Roulette mit einer Null, 37 gleich wahrscheinliche Ergebnisse.
**Eine Wette pro Runde**: direkt eine Zahl 0–36 anklicken oder Rot/Schwarz,
Gerade/Ungerade, 1–18/19–36, eines der drei Dutzende oder eine der drei Spalten.
Zahl: 36×; Dutzend/Spalte: 3×; andere Außenwetten: 2×. Die Null verliert bei
allen Außenwetten. Theoretische Rückzahlung 36/37 ≈ 97,2973 %.
Mehrere gleichzeitige Einsätze sowie Split/Street/Corner/Six-Line-Innenwetten
sind in dieser Oberfläche nicht enthalten. Das Zahlenfeld folgt der üblichen
Rot-/Schwarzzuordnung; es entscheidet kein Ergebnis im Client.

## Bilder austauschen

Acht einzelne transparente PNGs mit passenden VTEX-Deskriptoren:
`ui/AnoCore/styles/custom_game/anocore/arcade/{Symbol-ID}.png`.
Die Symbol-ID beeinflusst Regeln, die PNG nur die Darstellung. PNG unter
identischem Namen ersetzen; quadratisches Canvas, echte Transparenz,
zentriertes Motiv und vergleichbare sichtbare Größe beibehalten. Die neuen
Originalmotive wurden einzeln mit dem eingebauten Imagegen-Werkzeug erzeugt:
Orange, Trauben, Wassermelone, Goldglocke und grüne Sieben, jeweils glossy,
freigestellt, ohne Flammen, Rahmen, Partikel oder äußeren Glow.

Das UI bleibt clean schwarz/grün und verwendet das vorhandene ANOMEME-Banner.
Rahmen, Buttons, Text, Gewinnlinien und Roulette-Zahlen sind native UI-Elemente;
sie benötigen keine neu generierten Bilder. Bei kleinen Auflösungen ist das Fenster in der Höhe begrenzt und vertikal
scrollbar; das muss ebenfalls nativ geprüft werden. Grundlage sind die
[Panorama-Eigenschaften](https://developer.valvesoftware.com/wiki/CSGO_Panorama_CSS_Properties)
und [Scroll-Panel-Dokumentation](https://developer.valvesoftware.com/wiki/Panorama/Overview/Events).
Die Coinflip-/Roulette-Ergebnis-
animation ist derzeit eine native rotierende Ergebnisanzeige, kein fertiges
physikalisches Münz- oder Roulette-Radmodell. Dekorative Kartenbilder können
später ergänzt werden, ohne Buchungen oder Gewinnregeln zu ändern.

Nach PNG-Austausch oder Layoutänderung stets neu kompilieren und veröffentlichen:

```powershell
powershell -ExecutionPolicy Bypass -File .\ui\AnoCore\build.ps1 -Addon anomeme_ui
```

Alternativ separater Build `build-casino.ps1 -Cs2 "<CS2-Pfad>"`, Standardname
`anocasino_ui`. Kein doppeltes Mounten derselben Ressourcen aus zwei Addons.
Die kombinierte Version ist für den ersten Test am einfachsten.

## Datenbank und Fehlersicherheit

Ein Vorgang speichert eindeutige ID, Spieler, unveränderlichen Request inklusive
Regelversion und Ergebnis. Unter einer `FOR UPDATE`-Sperre des AP-Kontos werden
Einsatz, Zufallsergebnis, Auszahlung, Revision und Ledger gemeinsam committed.
Gleiche Vorgangs-ID und gleicher Request geben den gespeicherten Beleg zurück;
ein abweichender Request mit gleicher ID wird abgelehnt. Fehler rollen zurück.
Mehrere Server können dasselbe Konto über dieselbe Datenbank verwenden.

Tabellen: `ano_ap_accounts`, `ano_casino_operations`. Kein direkter Clientzugriff.
Keine Nachbuchung im Animations-Timer. Guthaben bleibt bei Abbruch auf dem Konto;
reservierte Mittel sind nach Wiederöffnung manuell zurückbuchbar. Alte
Ergebnisse behalten ihre gespeicherten Symbol-IDs und Auszahlungswerte auch bei
späteren Grafik-/Regeländerungen. AP-Erwerb über Gameplay und weitere Profil-
ansichten sind eigene Erweiterungen, nicht automatisch aktiv.

## Abnahme auf dem Testserver

Alle drei Spiele mit zwei berechtigten Spielern prüfen. Mehrfachklicks während
Buchung/Animation, Einsatzgrenzen, leeres Konto, unzureichende Reservierung,
Aufladen/Zurückbuchen, Textanzeige, zehn aufeinanderfolgende Spiele, Schließen
während Animation, Berechtigungsentzug, Reconnect, Mapwechsel, Hot Reload und
vollständigen Serverneustart testen. AP dürfen nicht entstehen/verschwinden,
außer durch dokumentierte Spielverluste/-gewinne oder Konsolengutschriften.
Konto nach Neustart und den letzten Beleg mit dem Zustand vor Abbruch vergleichen.

Kistensimulator und Wettzentrale bleiben getrennte Folgearbeiten. Der Book-artige
Bonus-Slot gehört zum Pflichtumfang, ist aber erst nach #429 spielbar.

## Kompatibilität und erweiterte Automaten

Migration 027 speichert Requests und Receipts als JSON mit den Standardoptionen
von `System.Text.Json`. Die Enum-Zahlen, Symbolreihenfolge, Regelversion und
repräsentative Auszahlungs-/JSON-Vektoren sind deshalb als Legacy-Vertrag
Regressionstests abgesichert. Belege bleiben unveränderlich: spätere Regel- oder
Darstellungsänderungen interpretieren abgeschlossene Vorgänge nicht neu.

Die Engines aus #424 (Blackjack), #425 (Risk / Double-or-Bank) und #426
(Book-style bonus slot) sind eine teilweise Erweiterung, keine derzeit spielbaren
Funktionen. Sie gelten erst nach #429 einschließlich Persistenz-, UI- und
CI-Integration als spielbar. Diese Dokumentation behauptet keine native
CS2-Abnahme der erweiterten Automaten.

## Erweiterte Spiele in der Lobby (#437)

Die Lobby öffnet Blackjack, Risk und Book neben den bestehenden drei Spielen.
Guthaben zuerst in der Lobby aufladen. Die neuen Ansichten nutzen denselben
Automatenbetrag und dieselbe Berechtigung `ano.casino.play`. **Zur Lobby** erhält
jede laufende Runde; beim nächsten Öffnen eines erweiterten Spiels wird die
aktive Runde automatisch fortgesetzt. Es gibt pro Spieler höchstens eine aktive
mehrstufige Runde. Einsätze bleiben bis zum Abschluss fix. Karten, verbleibendes
Deck, Bonusraster, Freispiele und Risk-Zwischenstände bleiben auf dem Server.

Blackjack: ein zufällig gemischtes vollständiges Deck, Dealer steht auf Soft 17,
Blackjack 3:2, höchstens zwei Hände, Split-Asse bekommen eine Karte. Hit, Stand,
Double und Split werden anhand der legalen Aktionen angeboten. Double und Split
buchen den zusätzlichen Handeinsatz atomar; die Endauszahlung erfolgt einmal.

Risk: Start reserviert den gewählten Einsatz als Zwischenstand. **Risk** hat
50 % Gewinnchance (Verdoppelung), sonst Verlust; **Bank** bucht den Zwischenstand
zurück. Nach fünf Versuchen bleibt Bank. Sofortiges Bank gibt den Einsatz zurück.

Book: fünf Walzen, drei Reihen, zehn Linien. Buch ist Wild und Scatter; drei
Bücher starten zehn Freispiele. Neue Scatter vergeben bis zu fünf weitere Spins,
mit höchstens 20 gleichzeitig verbleibenden und insgesamt 30 Freispielen. Das
expandierende Symbol bleibt fest. Freispiele kosten keinen zusätzlichen Einsatz;
die gesammelte Auszahlung wird am Rundenende gebucht. Die mathematischen Regeln
sind eigene versionierte Regeln und keine kommerzielle Automatenkopie.

**Buchung prüfen** wiederholt die unveränderte Aktion mit derselben ID. Ein
unbestätigter Vorgang blockiert weitere Spielaktionen. Die Buchungsgrenzen
reservieren auch während einer laufenden Runde den maximalen Auszahlungsraum.
Die Lobby-Verlaufsliste zeigt weiterhin die bisherigen Einzelspiele; die neuen
Runden besitzen ein separates dauerhaftes Aktionsledger. Native Windows-Builds,
Workshop-Auslieferung und Zwei-Client-Abnahme sind noch durchzuführen.

Die konkrete Installation steht in [casino-test-install.md](casino-test-install.md).

## Biggest Boy Bücher

Eigenständige siebte Slot mit 5×5-Feld, elf eigenen Symbolen und Kebap-Wild.
[Gewinnplan, Bonusregeln, mathematische Prüfung und Abnahme](biggest-boy.md).
`BiggestBoyMaximumStake` begrenzt den Einsatz auf bis zu 10.000 AP.

## Währungsanzeige

Alle Casino-Guthaben, Einsätze und Gewinne erscheinen mit `€`. Die Zahlen
bleiben unveränderte virtuelle Kontoeinheiten; dies ist keine Umrechnung in
Echtgeld. Bestehende `anoap`-Befehle und Datenbanknamen bleiben kompatibel.

## Biggest Boy: 5×3 und Sammel-Wilds (v3)

Neue Runden verwenden das versionierte Profil `anocore-biggest-boy-5x3-collector-v3`.
Fenster, Kontenanzeige, volle Walzenbreite, drei sichtbare Reihen und Bedienleiste
entsprechen der Freispiele-Slot. Die eigenen elf Bilder bleiben erhalten. Ein
kurzer doppelt belegter Symbolstreifen läuft kontinuierlich nach unten; die
Ergebniswalzen bremsen wie Book je 0,45 Sekunden ab und stoppen im Abstand von
0,2 Sekunden. Die Echtgeld-Optik ist ausschließlich eine Darstellung virtueller
Beträge; es gibt keine Ein- oder Auszahlung echten Geldes.

Zehn feste Linien zahlen von links nach rechts bei mindestens drei gleichen
Symbolen. Pro Linie zählt der höchste Gewinn, verschiedene Linien addieren sich.
Linieneinsatz ist Gesamteinsatz / 10. Der Gewinnplan im Spiel zeigt die komplette
Paytable und alle zehn Linien. Kebap ersetzt Gewinnsymbole, nie das Gesicht.

3/4/5 Gesichter im bezahlten Spin starten 10/15/20 Freispiele. Im Bonus erhält
jedes Bier einen gespeicherten Geldwert von 1×/2×/5× Einsatz mit Wahrscheinlichkeit
80/18/2 Prozent. Jeder sichtbare Kebap sammelt alle sichtbaren Bierwerte mit dem
aktuellen Sammlermultiplikator; mehrere Kebaps sammeln jeweils dieselbe Summe.
Ohne Kebap zahlen Bierwerte keine Sammlung. Normale Liniengewinne kommen hinzu.

Auch Kebaps ohne Bier zählen für den Bonusfortschritt. Je vier gesammelte Kebaps
geben zehn zusätzliche Freispiele und erhöhen die Stufe von ×1 auf ×2, ×3 und
schließlich ×10. Die neue Stufe gilt ab dem nächsten Spin. Mehrere in einem Spin
überschrittene Schwellen vergeben ihre Verlängerungen jeweils. Nach der dritten
Verlängerung gibt es keine weitere, also höchstens 50 Freispiele. Scatter im
Bonus verlängern nicht. Es gibt weder eine Symbolauswahl noch expandierende Walzen.

Der veröffentlichte Höchstgewinn pro Runde beträgt **5.000× Gesamteinsatz**.
Erreicht die Auszahlung diese Grenze, wird der verbleibende Betrag bis zum
Höchstgewinn ausgezahlt und die Runde endet sofort, auch bei restlichen Freispielen.
Der Presenter meldet dies ausdrücklich. Die Reservierung deckt diesen Betrag ab;
Einsatz und Gesamtauszahlung werden wie bisher atomar und dauerhaft verbucht.

Das eigene Modell übernimmt keine unveröffentlichten Originalquoten von Big Bass.
`python tools/audit_biggest_boy_collector.py` berechnet aus den festen Walzen,
Linien, Wild-/Bier-Korrelationen und Bonusstufen die exakte Erwartung **vor** der
Höchstgewinnregel: 93,815683 % als obere Grenze der tatsächlichen RTP. Die
Bonuswahrscheinlichkeit ist 0,19702656 % pro bezahltem Spin. Die Höchstgewinnregel
senkt die tatsächliche RTP; die obere Grenze ist keine zertifizierte RTP-Angabe.

Bestehende 5×5-v2-Runden behalten ihre Regeln, Symbolauswahl, gespeicherten Raster
und fünf Reihen bis zum Abschluss. Nur neue Runden beginnen mit v3. Die alte
Engine und ihre Regressionen bleiben erhalten. Kein Zurücksetzen aktiver Runden.

Native Abnahme: beide Spiele öffnen, volle Breite, fünf Walzen mit drei Reihen
und sichtbare Bedienleiste prüfen; Biggest Boy mehrfach drehen und gestaffelte
Stopps prüfen. Bonus: Bierwerte, mehrere Kebaps, Wildzähler, Stufenwechsel und
Fortsetzung nach Schließen kontrollieren. Eine bestehende v2-Runde bis zum Ende
fortsetzen. Valve-Kompilierung und CS2-Abnahme sind vor Veröffentlichung erforderlich.
