# Alles Spitze (AnoCasino CS Edition)

Risk wird in der Lobby durch Alles Spitze ersetzt. Die historische Spielversion anocasino-risk-v1 und ihre numerische Datenbank-ID bleiben für bereits laufende Runden erhalten. Die neue Spielversion lautet anocasino-spitze-v1. Alle Beträge sind ausschließlich virtuell (€).

## Walze und Auszahlung

Eine Walze zeigt fünf CS-inspirierte Skin-Symbole und Leerfelder. Der serverseitige Zufall entscheidet das Ergebnis, die Animation ist ausschließlich kosmetisch.

| Symbol | Effekt | AnoCasino-Häufigkeit |
| --- | --- | ---: |
| GG Spray | Alle drei Türme und den Cashpot zurücksetzen | 16 % |
| Safari Mesh | Niedrigen Turm erhöhen | 27 % |
| Neon Revolution | Mittleren Turm erhöhen | 21 % |
| Gold | Hohen Turm erhöhen | 10 % |
| Messer | Joker: alle drei Türme erhöhen | 10 % |
| Leerfeld | Kein Fortschritt | 16 % |

Die drei fünfstufigen Gewinnleitern geben folgende Einsatzmultiplikatoren vor:

| Stufe | Safari Mesh | Neon Revolution | Gold |
| --- | ---: | ---: | ---: |
| 1 | 1× | 3× | 10× |
| 2 | 2× | 8× | 40× |
| 3 | 4× | 16× | 100× |
| 4 | 7× | 29× | 200× |
| 5 | 10× | 50× | 350× |

Der Cashpot ist die Summe der drei aktuell höchsten beleuchteten Turmstufen mal festem Rundeinsatz. Erreichen oder wiederholtes Treffen einer vollen Stufe 5 gewährt zusätzlich einen **Direkterfolg** in Höhe ihres Höchstwerts. Direkterfolge bleiben auch beim Verlustsymbol erhalten und werden erst bei endgültiger Auszahlung einmalig gutgeschrieben.

Das erste vollständige Füllen aller drei Türme startet sieben kostenlose Action Games. Diese sind weitere Walzenzüge ohne Einsatzabbuchung, können erneut Direkterfolge auslösen und sind vor weiteren bezahlten Spins abzuschließen. Nach dem siebten Action Game wird automatisch abgerechnet. Über CASHPOT EINSAMMELN lässt sich eine aktive Runde jederzeit abschließen: Cashpot plus Direkterfolge werden genau einmal ausgezahlt. Ohne Bonus endet eine Runde nach spätestens 100 bezahlten Spins.

**Hinweis:** Reel-Gewichte, Multiplikatoren, Sicherheitsgrenze und konkrete Bonus-Auszahlung sind eigenständige Version-1-Regeln für AnoCasino. Die offiziellen Merkur-Quellen veröffentlichen nicht den vollständigen mathematischen Reel-Plan; deshalb wird **keine originale oder zertifizierte RTP** behauptet.

## Persistenz, Limits und Schutz

- Neuer Einsatzstandard CasinoMaximumStakes.Spitze = 100000 virtuelle €; erlaubt sind 50 bis 1000000, in 50er-Schritten. Der Wert gilt nur für neue Runden.
- Jeder akzeptierte bezahlte Spin bucht einmal den Rundeneinsatz. Action Games und Einsammeln kosten nichts; während der Runde bleibt die Einsatzhöhe gesperrt.
- Konservative maximal reservierte Rundenauszahlung: (100 + 7 + 1) × 410 × Einsatz; bei 100000 sind das 4428000000 virtuelle €.
- Die bestehende Kontoobergrenze und mögliche Reservierungen werden vor der Zufallsziehung geprüft. Die Datenbank schützt Wiederholungsanfragen mit Operations-ID und Revision.
- Historische Risk-Runden und die dazugehörigen Regeln, Dateien und IDs dürfen nicht entfernt werden.

## UI und Installation

Die Lobby verwendet aus Kompatibilitätsgründen weiterhin die Button-ID casino_lobby_risk, öffnet damit jedoch Alles Spitze. Das neue Panorama-Layout und die CSS-Walzenanimation liegen in ui/AnoCore/layout/custom_game/anocore/ano_casino_rounds.xml und ui/AnoCore/styles/custom_game/anocore/ano_casino_rounds.css. Momentan zeigen die Symbolfelder gestaltete **Skin-Namen**, da die in einem früheren Chat generierten PNG-Dateien nicht im GitHub-Repository vorliegen. Sie sind keine angeblich bereits integrierten Skin-Bilder.

Zum lokalen Test sind ein passendes AnoCore-Serverplugin und die aus denselben Quellen neu kompilierte anomeme_ui-Workshop-UI erforderlich; der alte risk.vtex bleibt ausschließlich für gespeicherte Legacy-Runden erhalten. Keine Änderung direkt am Live-Server ohne Abnahme.

Auf einem Testserver mit zwei Clients prüfen: alle sechs Ergebnisarten, sichtbare Walzenbewegung, 15 Stufenlichter, Cashpot, Direkterfolge, Einsammeln, sieben Action Games, 100er-Grenze, 100000-Einsatz, Duplikat/Retry, Reconnect, Legacy Risk, Fehlermeldungen und Client-Panorama-Parsing. Normale .xml/.css-Dateien ersetzen keine Valve-kompilierten UI-Ressourcen. Auch nach grüner CI bleibt die CS2-Abnahme Pflicht.

Referenzen: https://merkur-spielewelt.de/spiele/alle-spiele/alles-spitze.html und https://merkur.com/spiele/beitraege/alles-spitze.html
