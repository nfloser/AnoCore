# Biggest Boy Bücher – originales 5×5-Regelprofil

Eigenständige siebte AnoCasino-Kachel; AnoBook bleibt unverändert. Einstieg:
`!anocasino` → **BIGGEST BOY BÜCHER**. Ausschließlich virtuelle AnoPoints.
Die neuesten elf vom Nutzer gelieferten Bilder ersetzen die frühere Symbolidee:
Kebap ist Wild und expandiert im Bonus, das Gesicht ist ausschließlich Scatter.
Ein zusätzliches Buchsymbol gehört nicht zu diesem Profil.

## Einsatz und Gewinnplan

Gesamteinsatz: 50–10.000 AP in 50er-Schritten. Die Runtime-Konfiguration
`BiggestBoyMaximumStake` begrenzt ihn serverseitig und in der Oberfläche;
Standard: 10.000 AP. Änderung wird beim Pluginstart übernommen. Bereits aktive
Bonusrunden behalten ihren gespeicherten Einsatz auch nach einer Absenkung.
Andere Spiele behalten ihre bisherigen Einsatzgrenzen.

Zehn immer aktive Linien; Linieneinsatz = Gesamteinsatz / 10. Mindestens drei
zusammenhängende gleiche Gewinnsymbole ab der linken Walze. Kebap ersetzt alle
Gewinnsymbole, niemals Scatter. Pro Linie zählt genau die höchstwertige passende
Kombination; Liniengewinne werden addiert. Keine Vergütung für rechte,
unterbrochene oder nicht auf einer aktiven Linie liegende Kombinationen.

| Symbol | 3 gleiche | 4 gleiche | 5 gleiche |
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

Die Faktoren beziehen sich auf den **Linieneinsatz**, nicht den Gesamteinsatz.
Beispiel: Einsatz 100 AP; eine Linie mit fünf Bier-Symbolen zahlt
100 / 10 × 2250 = **22.500 AP brutto**. Gibt es keine weiteren Gewinne, beträgt
der Nettogewinn des bezahlten Spins 22.400 AP. Die große Gewinnanzeige zeigt
die Bruttoauszahlung des aktuellen Spins. Kontoauszahlung und Bonusgesamtgewinn
werden zusätzlich getrennt angezeigt.

## Gewinnlinien

Zeilen 1 bis 5 von oben; jede Tabellenzeile nennt die Zeile auf Walze 1 bis 5.

| Linie | Walze 1 | Walze 2 | Walze 3 | Walze 4 | Walze 5 |
|---|---:|---:|---:|---:|---:|
| 1 | 1 | 1 | 1 | 1 | 1 |
| 2 | 2 | 2 | 2 | 2 | 2 |
| 3 | 3 | 3 | 3 | 3 | 3 |
| 4 | 4 | 4 | 4 | 4 | 4 |
| 5 | 5 | 5 | 5 | 5 | 5 |
| 6 | 1 | 2 | 3 | 4 | 5 |
| 7 | 5 | 4 | 3 | 2 | 1 |
| 8 | 1 | 2 | 3 | 2 | 1 |
| 9 | 5 | 4 | 3 | 4 | 5 |
| 10 | 3 | 2 | 1 | 2 | 3 |

## Freispiele und Expansion

- Drei, vier oder fünf Gesicht-Scatter irgendwo auf dem Feld: **10 Freispiele**.
- Im Bonus lösen erneut mindestens drei Gesichter **+5 Freispiele** aus.
- Höchstens **30 Freispiele insgesamt**, höchstens **20 verbleibende**; am Limit
  werden nur noch die tatsächlich verfügbaren zusätzlichen Spins vergeben.
- Der Scatter selbst zahlt keinen AP-Gewinn und ersetzt keine Gewinnsymbole.
- Nur in Freispielen expandiert ein Kebap auf alle Nicht-Scatter-Felder seiner
  Walze. Gesichter bleiben sichtbar und können erneut Freispiele auslösen.
- Linien werden anhand des endgültigen, gegebenenfalls expandierten Feldes
  bewertet. Es gibt keine zusätzliche zweite Vergütung des Rohfelds.
- Nur der erste Spin wird bezahlt. Einsatz und Regelversion bleiben gespeichert.
  Alle Bruttogewinne der Runde werden am Bonusende einmal gutgeschrieben.

## Walzen und mathematische Prüfung

Fünf feste kreisförmige Streifen mit jeweils 100 Positionen; je Walze ein
kryptografisch gleichverteilter Startindex. Die fünf sichtbaren Felder sind
aufeinanderfolgende Positionen desselben Streifens. Je Streifen: ein Kebap,
zwei mindestens fünf Felder voneinander entfernte Scatter, 6 Bier, 6 Zigaretten,
7 Butter, 10 Shots, 10 Trichter, 14 G, 14 O, 15 A, 15 T.

`BiggestBoyRules` enthält den vollständigen deterministischen Streifenaufbau.
Panorama verwendet dieselben Streifen mit zwei Kopien plus fünf Randfeldern.
Vier vollständige Umläufe laufen nach unten; Walzen enden zeitversetzt bei den
gebuchten Stopppositionen. Die Endansicht zeigt das autoritative Feld und markiert
Gewinnfelder sowie expandierte Walzen. Animation erzeugt keine Ergebnisse.

`python3 tools/audit_biggest_boy.py` liest Linien, Faktoren und Häufigkeiten aus
den Regeln, summiert die exakt möglichen Liniengewinne und berechnet die
begrenzte Bonuslänge rekursiv. Keine Übernahme proprietärer Automaten-Odds.

| Messgröße | Wert |
|---|---:|
| Bonuschance je bezahltem Spin | 0,856 % |
| Mittlerer Abstand zwischen Bonusstarts | 116,8224 bezahlte Spins |
| Erwartete Bonuslänge einschließlich Retrigger | 10,4471136 Freispiele |
| Basis-RTP | 76,54132755 % |
| Gesamt-RTP einschließlich Bonus | **94,18222789 %** |
| Konservative maximale Rundenauszahlung | 77.500 × Gesamteinsatz |

Bonuswahrscheinlichkeit: jede Walze zeigt mit 10/100 Wahrscheinlichkeit genau
einen Scatter, also P(mindestens 3 von 5) = 0,00856. Linienkorrelationen ändern
den Erwartungswert ihrer Summe nicht. Die Audit-Rechnung berücksichtigt pro
Linie den höchsten kompatiblen Gewinn und die Bonus-Expansion. Erwarteter RTP
ist kein garantierter Gewinn einzelner Sessions. Das frühere 94,25-%-Konzept war
vorläufig; dieses endgültige Profil hat die oben berechneten Werte.

## Persistenz und Kompatibilität

Neue Enum-Werte werden angehängt; bestehende numerische Spielarten bleiben
unverändert. Das vorhandene `game_kind`-Feld unterstützt das neue Spiel ohne
Schemaänderung. Regelversion: `anocore-biggest-boy-5x5-v1`. Vorhandene Bonus-
und Blackjack-/Risk-Runden werden weiter durch ihre ursprünglichen Regeln
geführt. Account-/Rundenlocks, Revisionsprüfung, Permission-Prüfung und
idempotente Aktions-IDs bleiben auf dem bestehenden Buchungsweg.

## Native Abnahme vor Workshop-Veröffentlichung

1. Passendes Plugin und vollständiges Addon aus demselben Build verwenden.
   `build.ps1 -Addon anomeme_ui -InstallLocalClient` kompiliert alle elf Texturen,
   Casino-Ansichten und das bisherige Veto. Die bisherigen Serverkonfigurationen
   beibehalten; nur den neuen Einsatzparameter bei Bedarf ergänzen.
2. Lokal `!anocasino` öffnen. Siebte Kachel durch Scrollen erreichen, Biggest Boy
   öffnen; 25 lesbare Bilder und Regelnansicht prüfen. Alle sechs alten Spiele
   weiterhin öffnen. Keine CustomHud-/Valve-Parserfehler.
3. Mehrfach spielen: vier vollständige kontinuierliche Umläufe nach unten,
   zeitversetzte Stops und Neustart der Animation beim folgenden Spin prüfen.
   Gebühren und angezeigte Endbilder mit den Serverbelegen abgleichen.
4. Gewinne anhand der Linien im Gewinnplan nachrechnen. Gewinnfelder, große
   Bruttoanzeige und Bonusgesamtgewinn müssen übereinstimmen. Ungewinnende Spins
   dürfen keinen Gewinn melden.
5. Im Testserver Bonus abnehmen: Scatter, +5, Expansion mit erhaltenen Gesichtern,
   gesperrter Einsatz und genau eine Kontoauszahlung am Bonusende. Menü schließen,
   wieder öffnen, reconnecten und Plugin neu starten: gespeicherte Runde muss
   fortgesetzt werden. Einsatzlimit absenken: vorhandene Runde bleibt spielbar.
6. Doppelklick, fehlende AP, Spieler ohne Casino-Recht, Konto nahe dem AP-Limit
   und andere aktive Rundenspiele prüfen. Kein Doppelabbuchen. Veto mitprüfen.
7. Erst nach dieser Abnahme bestehendes Workshop-Addon aktualisieren; danach
   ohne lokale Overrides die tatsächliche Workshop-Auslieferung testen.

Valve-Ressourcenkompilierung und echte CS2-Eingabe/Performance lassen sich durch
Quelltests allein nicht bestätigen. Keine automatische Produktionsfreigabe.

## Lokale Qualitätsprüfung dieses Kandidaten

- Vollständiger Release-Build: erfolgreich, 0 Warnungen/Fehler.
- 1.364 .NET-Tests mit lokalem MariaDB 10.11.14: erfolgreich, 0 übersprungen.
  Dabei ist ausschließlich `ManagementPipeBridgeTests` ausgenommen: die
  Umgebung erlaubt die nötigen Unix-Sockets nicht. Ein ungefilterter Lauf
  scheitert bei sechs Pipe-Tests mit `Permission denied`; keine Tests entfernt.
- Alle neuen Regeln-, Presenter-, Routing-, Einsatz- und MariaDB-Replaytests
  bestanden, inklusive Wiederaufnahme nach reduziertem Einsatzmaximum.
- Python-Operatorprüfungen: 60 erfolgreich, 18 PowerShell-Prüfungen mangels
  PowerShell übersprungen (78 insgesamt).
- Whitespace-Formatprüfung und `git diff --check`: erfolgreich. Die vollständige
  semantische `dotnet format`-Prüfung ist lokal ebenfalls wegen der benötigten
  MSBuild-Pipe blockiert; sie bleibt Bestandteil der vorhandenen GitHub-CI.
- Originaltreue aller elf PNGs byteweise geprüft; beide Addon-Builder enthalten
  sämtliche elf Texturdeskriptoren.
- Eigenreview: getrennte Spielart/Version, erhaltene Legacy-Grenzen, auf
  775 Mio. AP begrenzte maximale Reserve bei 10.000 AP Einsatz, erhaltene
  Scatter bei Expansion, nur ein Gewinn pro Linie, gesperrte Animation,
  Konto-/Rundenlocks und idempotente Wiederholung. HUD-Klassenänderungen werden
  zwischengespeichert; sieben verschachtelte Verschiebungen statt 100 möglicher
  Positionsklassen je Walze reduzieren laufende Updates.
- GitHub-Push, PR, CI und Merge stehen aus: automatische Freigabeprüfung
  verlangt die ausdrückliche Freigabe für die öffentliche Veröffentlichung
  der beigefügten Bilder. Native Valve-/CS2-Abnahme steht ebenfalls aus.
