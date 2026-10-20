# Biggest Boy Bücher – 5×5-Regelprofil v2

Eigenständige AnoCasino-Slot neben AnoBook. Einstieg:
`!anocasino` → **BIGGEST BOY BÜCHER**.

Alle angezeigten €-Beträge sind ausschließlich virtuelle Casino-Werte. Es gibt
keine Echtgeld-Einzahlung, Auszahlung oder Umrechnung.

## Symbole

- Kebap: Wild; ersetzt jedes normale Gewinnsymbol, niemals Scatter.
- Bier, Zigarette, Butter, Shot, Trinktrichter, G, O, A, T: normale Gewinnsymbole.
- Gesicht: Scatter; zahlt nicht direkt und wird nie durch Wild oder Expansion ersetzt.

## Einsatz

Zugelassene Einsatzstufen:

`100 / 250 / 500 / 1.000 / 2.500 / 5.000 / 10.000 €`

`BiggestBoyMaximumStake` kann die maximal auswählbare Stufe serverseitig
begrenzen. Bereits laufende Bonusrunden behalten ihren gespeicherten Einsatz.

## Feld und Gewinnlinien

- 5 Walzen × 5 sichtbare Symbole.
- 25 immer aktive Gewinnlinien.
- Linieneinsatz = Gesamteinsatz / 25.
- Gewinne laufen ausschließlich von links nach rechts.
- Mindestens drei zusammenhängende passende Symbole ab Walze 1.
- Kebap kann normale Symbole ersetzen.
- Pro Basislinie zählt nur die höchstwertige passende Kombination.

Die 25 Linien sind in `BiggestBoyRules.Paylines` eingefroren und Teil der
Regelversion `anocore-biggest-boy-5x5-v2`.

## Basis-Gewinnplan

| Symbol | 3 | 4 | 5 |
|---|---:|---:|---:|
| Kebap / Wild | 225× | 1125× | 2500× |
| Bier | 170× | 675× | 2250× |
| Zigarette | 135× | 450× | 1690× |
| Butter | 113× | 338× | 1125× |
| Shot | 68× | 225× | 675× |
| Trinktrichter | 56× | 169× | 563× |
| G | 22× | 56× | 169× |
| O | 22× | 56× | 169× |
| A | 11× | 45× | 113× |
| T | 11× | 45× | 113× |

Die Multiplikatoren beziehen sich auf den Linieneinsatz.

## Freispiele

Der Scatter-Trigger wird über das vollständige 5×5-Feld gezählt:

- 3 Scatter: 10 Freispiele.
- 4 Scatter: 15 Freispiele.
- 5 Scatter: 20 Freispiele.

Während der Freispiele geben mindestens 3 Scatter jeweils **+5 Freispiele**.
Über die gesamte Bonusrunde werden höchstens **30 Freispiele** vergeben.

Nur der bezahlte Startspin belastet den Automaten. Symbolauswahl und Freispiele
haben keinen zusätzlichen Einsatz. Der Einsatz bleibt für die komplette
Bonusrunde gesperrt und wird zusammen mit der Regelversion persistiert.

## Expanding-Symbol

Nach einem Bonus-Trigger muss genau einmal eines dieser Symbole gewählt werden:

Bier, Zigarette, Butter, Shot, Trinktrichter, G, O, A oder T.

Kebap und Scatter sind nicht auswählbar.

Wenn das gewählte Symbol in einem Freispin auf einer Walze vorkommt, füllt es
alle Nicht-Scatter-Felder dieser Walze. Scatter bleiben an ihrer ursprünglichen
Position sichtbar und zählen weiterhin für Retrigger.

Der gespeicherte Spin enthält sowohl `RawGrid` als auch das expandierte
`Grid`. Dadurch bleiben Ergebnis, Retrigger und Darstellung nachvollziehbar.

Die Expansion verwendet einen eigenen kleinen Zusatz-Gewinnplan. Basisgewinne
werden auf dem unveränderten Rohfeld bewertet; Expansion-Gewinne werden separat
addiert. Dadurch bleiben häufige Buchstaben mathematisch kontrolliert.

| Gewähltes Symbol | Expansion 3 | Expansion 4 | Expansion 5 |
|---|---:|---:|---:|
| Bier | 15× | 59× | 198× |
| Zigarette | 16× | 54× | 204× |
| Butter | 11× | 32× | 106× |
| Shot | 2× | 8× | 24× |
| Trinktrichter | 3× | 8× | 26× |
| G | 1× | 2× | 6× |
| O | 1× | 2× | 6× |
| A | 1× | 2× | 5× |
| T | 1× | 2× | 5× |

## Walzen und Animation

Jede Walze besitzt einen festen kreisförmigen Streifen mit 100 Positionen.
Der Server wählt pro Walze kryptografisch gleichverteilt einen Stop.

Die native Panorama-Darstellung verwendet denselben festen Streifen, vier
vollständige Abwärts-Umläufe und zeitversetzte Stops. Die Animation entscheidet
niemals über das Ergebnis; sie zeigt ausschließlich das bereits serverseitig
gebuchte Ergebnis.

## Mathematischer Audit

`python3 tools/audit_biggest_boy.py` liest Linien, Paytables und
Walzenhäufigkeiten direkt aus dem C#-Regelprofil und berechnet den Erwartungswert
ohne Monte-Carlo-Abhängigkeit.

Wesentliche Werte:

- Bonuschance pro bezahltem Spin: **0,856 %**
- mittlerer Abstand zwischen Bonusstarts: **116,8224 Spins**
- erwartete Bonuslänge inklusive 10/15/20-Start und Retrigger:
  **10,7338847142 Freispiele**
- Basis-RTP: **76,54132755 %**
- Gesamt-RTP abhängig vom gewählten Expanding-Symbol:
  ungefähr **93,90 % bis 94,89 %**
- Bier als Expanding-Symbol: **94,1747510434 %**
- konservative maximale Rundenauszahlung:
  höchstens **83.824 × Einsatz**

Der Erwartungswert beschreibt lange Serien und garantiert keinen Gewinn in
einer einzelnen Session.

## Persistenz und Sicherheit

- Regelversion: `anocore-biggest-boy-5x5-v2`.
- Eigene persistente Spielart `CasinoRoundKind.BiggestBoy`.
- Symbolauswahl wird als eigene idempotente Rundenaktion gespeichert.
- Die Auswahl belastet das Guthaben nicht erneut.
- Freispins verwenden den gespeicherten Einsatz.
- Account-/Rundenlocks, Revisionsprüfung, Permission-Prüfung und Replay-Schutz
  bleiben auf dem bestehenden Casino-Buchungsweg.
- Auszahlung erfolgt serverseitig; Panorama ist reine Darstellung.

## Native Abnahme

Vor einem Workshop-/Produktions-Update:

1. Plugin und `anomeme_ui` aus demselben Commit bauen.
2. `!anocasino` öffnen und Biggest Boy aufrufen.
3. Alle 25 Symbole sichtbar und ohne Panorama-Parserfehler prüfen.
4. Einsatzstufen vollständig durchschalten.
5. 3/4/5-Scatter-Trigger prüfen.
6. Bonuspicker für alle neun erlaubten Symbole prüfen; Kebap/Scatter dürfen
   nicht auswählbar sein.
7. Vier Abwärts-Umläufe, zeitversetzte Stops und wiederholte Spins prüfen.
8. Raw-/Expanded-Ergebnis, markierte Gewinnlinien und Gewinnanzeige gegen
   Serverbelege vergleichen.
9. Menü schließen, reconnecten und Plugin neu starten: aktive Bonusrunde und
   ausgewähltes Symbol müssen fortgesetzt werden.
10. Kein Doppelabbuchen bei Doppelklick, Retry oder Symbolauswahl.
11. Danach vollständiges Addon im Workshop aktualisieren und erneut ohne lokale
    Overrides testen.

Native Valve-Kompilierung, tatsächliche CS2-Eingabe und Rendering-Performance
müssen weiterhin im Client abgenommen werden.
