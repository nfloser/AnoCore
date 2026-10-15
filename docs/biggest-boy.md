# Biggest Boy Slots — verbindliches 5×3-Profil

Status: Implementierung/Tests in Arbeit; **keine native Produktionsfreigabe**.
Neue Regelversion: `anocore-biggest-boy-5x3-expanding-v4`. Neue Runden verwenden
exakt fünf Walzen mit je drei sichtbaren Symbolen, insgesamt 15 Positionen.
Das ersetzt für neue Spiele sowohl das frühere 5×5-Profil als auch den Collector-Bonus.

## Vorlage und Bedienung

Die zehn Gewinnlinien stammen direkt aus `BonusSlotRules.Paylines`. Fensterhülle,
Kontoanzeigen, Einsatz- und Freispielaktionen sowie Navigation sind aus der
funktionierenden Freispiele-XML übernommen. Biggest Boy hat eine eigene native
XML-/CSS-Ressource. Book-XML, Book-CSS und `BonusSlotRules.cs` bleiben bytegleich;
`biggest-boy-v4-source-lock.json` und Regressionen prüfen das.

Spielaufruf: `!anocasino` → Biggest Boy. Dieselben Berechtigungen, Konten,
Einsatzschritte, Transaktionen, Operation-IDs, Restore- und Abrechnungswege bleiben
in Gebrauch. Die Darstellung mit € bezeichnet weiterhin ausschließlich virtuelle
AP-Beträge. Einsatz und mathematisches Modell werden mit der Runde eingefroren.

## Symbole und Linienauszahlungen

Ein eigenes goldenes Buch ist ausschließlich Wild. Döner ist das höchste reguläre
Symbol. Das Gesicht ist ausschließlich Scatter, niemals Wild. Die übrigen Bilder
sind unverändert jeweils eigenständige PNG-Dateien. Enum-IDs 0–10 bleiben wegen
alter gespeicherter Runden stabil; das Buch erhält ID 11.

Drei, vier oder fünf passende Symbole müssen auf einer gültigen Linie von links
aufeinanderfolgen. Das Buch ersetzt jedes reguläre Symbol. Pro Linie wird nur
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
| Goldenes Buch / Wild | 1 | Ersetzt reguläre Symbole | Ersetzt reguläre Symbole | Ersetzt reguläre Symbole |

Alle fünf Walzen haben 100 feste zyklische Positionen. Scatter liegen an 0/50,
das Buch an 25. Die 97 regulären Positionen werden deterministisch aus den oben
aufgeführten Häufigkeiten angeordnet: Index `(n × 37 + Walze × 19) mod 97`.
Die tatsächlichen vollständigen Streifen sind reproduzierbar in den generierten
Panorama-Panels und in `BiggestBoyExpandingRules.ReelStrips` enthalten.

## Freispiele und Expansion

3/4/5 Gesichter im Rohfeld vergeben 10/15/20 Freispiele. Beim Bonusstart wird
einmal gleichverteilt eines der zehn regulären Symbole gezogen und gespeichert;
es gibt keine zusätzliche Auswahloberfläche. Bücher und Scatter sind ausgeschlossen.
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
Animation läuft 30–129 Positionen abwärts, benötigt 5,2–6,0 Sekunden je nach Walze
und stoppt links nach rechts im Abstand von 0,2 Sekunden. Jede 80px-Bewegung hat
eine kurze Haltephase (20 % des Schritts). Der 200ms-Vorbereitungszustand setzt
den Streifen vor Aktivierung der Animation. Das Ende ist exakt auf den gebuchten
Stop ausgerichtet; kein Austausch gegen ein unverbundenes Ergebnisband.

Die 303 statischen Panels je Walze und die 100 endlichen Reisevarianten sind
begrenzt und werden reproduzierbar generiert. Die Bewegung läuft im Client ohne
per-frame Netzwerkverkehr. Für alle 10.000 Start-/Zielpaare wird die korrekte
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

Rohdaten und vollständige Paytable: `biggest-boy-v4-audit.json`.
Reproduzieren:

```bash
python tools/audit_biggest_boy_expanding.py
dotnet run --project tools/AnoCore.BiggestBoyAudit -c Release -- 2000000 20261015
python tools/generate_biggest_boy_ui.py
```

## Fehlende Originaldatei und native Freigabe

Die elf regulären/Scatter-PNGs liegen vor. Die eigenständige Originaldatei für
das **goldene Buch** wurde bisher nicht im Repository oder den zugeordneten
Bildanhängen gefunden. Sie wird nicht durch ein anderes Symbol ersetzt.
`python tools/check_biggest_boy_assets.py` meldet den fehlenden Pfad explizit:
`ui/AnoCore/styles/custom_game/anocore/casino/biggest_book.png`.

Der bestehende Addon-Build bleibt ohne v4-Anforderung verwendbar. Der dedizierte
v4-Build stoppt bei fehlendem Originalbild, bevor er das Addon verändert:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\ui\AnoCore\build-biggest-boy.ps1 -Addon anomeme_ui -BiggestBoyWildPng "C:\Pfad\Original-Buch.png" -InstallLocalClient
```

Noch erforderlich: Original-Buch-PNG hinzufügen, sämtliche zwölf PNG/VTEX-Paare
prüfen, Valve-Ressourcen kompilieren und im nativen Client zweimal bezahlte Spins
sowie eine Bonusserie testen. Schließen/Reconnect, Einsatzsperre, 30er-Grenze,
Summen, Bildlauf, aktive Linien und Parserlogs prüfen. Keine Workshop-Veröffentlichung
oder Produktions-Merge vor dieser Abnahme.

## Gespeicherte frühere Runden

Bereits akzeptierte v2-/v3-Runden behalten ihre historischen Regeln und maximale
Reservierung bis zum Abschluss. Sie werden nicht in v4 umgerechnet. Der alte HUD
bleibt nur für solche gespeicherten Runden zuständig; neue Biggest-Boy-Runden
verwenden ausschließlich v4 und das 5×3-HUD. Das historische v2-Profil ist separat
in `biggest-boy-legacy-v2.md` dokumentiert.
