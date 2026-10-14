# AnoCasino – spielbare Version und Bildertausch

Diese Version ergänzt den separaten Drei-Frucht-Animationstest. Der bestehende Stand\n`anocasino-v1` bleibt der Legacy-Vertrag für gespeicherte Vorgänge; neue\nSpiel-Engines dürfen dessen Auswertungen und Belege nicht verändern. Slots, Coinflip
und Roulette sind serverseitig spielbar. Die native CS2-Darstellung und alle
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

Server neu starten. Schema-Migration 27 erzeugt AP-Konten und Vorgangsledger;
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

Kistensimulator, Wettzentrale und Book-Automat bleiben getrennte Folgearbeiten.
\n\n## Kompatibilität und erweiterte Automaten\n\nMigration 027 speichert Requests und Receipts als JSON mit den Standardoptionen\nvon `System.Text.Json`. Die Enum-Zahlen, Symbolreihenfolge, Regelversion und\nrepräsentative Auszahlungs-/JSON-Vektoren sind deshalb als Legacy-Vertrag\nRegressionstests abgesichert. Belege bleiben unveränderlich: spätere Regel- oder\nDarstellungsänderungen interpretieren abgeschlossene Vorgänge nicht neu.\n\nDie Engines aus #424 (Kistensimulator), #425 (Wettzentrale) und #426\n(Book-Automat) sind eine teilweise Erweiterung, keine derzeit spielbaren\nFunktionen. Sie gelten erst nach #429 einschließlich Persistenz-, UI- und\nCI-Integration als spielbar. Diese Dokumentation behauptet keine native\nCS2-Abnahme der erweiterten Automaten.\n