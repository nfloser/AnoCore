# Alles Spitze (AnoCasino CS Edition)

Risk wird in der Lobby durch Alles Spitze ersetzt. Die historische Spielversion anocasino-risk-v1 und ihre numerische Datenbank-ID bleiben für bereits laufende Runden erhalten. Die neue Spielversion lautet anocasino-spitze-v1. Alle Beträge sind ausschließlich virtuell (€).

## Walze und Auszahlung

Eine Walze zeigt fünf CS-inspirierte Skin-Symbole und Leerfelder. Der serverseitige Zufall entscheidet das Ergebnis, die Animation ist ausschließlich kosmetisch.

| Symbol | Effekt | AnoCasino-Häufigkeit |
| --- | --- | ---: |
| GG Spray | Alle drei Türme und den Cashpot zurücksetzen | 25 % |
| Safari Mesh | Niedrigen Turm erhöhen | 28 % |
| Neon Revolution | Mittleren Turm erhöhen | 22 % |
| Gold | Hohen Turm erhöhen | 6 % |
| Messer | Joker: alle drei Türme erhöhen | 4 % |
| Leerfeld | Kein Fortschritt | 15 % |

Die drei fünfstufigen Gewinnleitern geben folgende Einsatzmultiplikatoren vor:

| Stufe | Safari Mesh | Neon Revolution | Gold |
| --- | ---: | ---: | ---: |
| 1 | 0,2× | 0,6× | 2× |
| 2 | 0,4× | 1,2× | 6× |
| 3 | 0,8× | 3× | 24× |
| 4 | 2× | 8× | 100× |
| 5 | 6× | 30× | 300× |

Der Cashpot ist die Summe der drei aktuell höchsten beleuchteten Turmstufen mal festem Rundeinsatz. Erreichen oder wiederholtes Treffen einer vollen Stufe 5 gewährt zusätzlich einen **Direkterfolg** in Höhe ihres Höchstwerts. Direkterfolge bleiben auch beim Verlustsymbol erhalten und werden erst bei endgültiger Auszahlung einmalig gutgeschrieben.

Das erste vollständige Füllen aller drei Türme startet sieben kostenlose Action Games. Diese sind weitere Walzenzüge ohne Einsatzabbuchung, können erneut Direkterfolge auslösen und sind vor weiteren bezahlten Spins abzuschließen. Nach dem siebten Action Game wird automatisch abgerechnet. Über CASHPOT EINSAMMELN lässt sich eine aktive Runde jederzeit abschließen: Cashpot plus Direkterfolge werden genau einmal ausgezahlt. Ohne Bonus endet eine Runde nach spätestens 100 bezahlten Spins.

**Hinweis:** Reel-Gewichte, Multiplikatoren, Sicherheitsgrenze und konkrete Bonus-Auszahlung sind eigenständige Version-1-Regeln für AnoCasino. Die offiziellen Merkur-Quellen veröffentlichen nicht den vollständigen mathematischen Reel-Plan; deshalb wird **keine originale oder zertifizierte RTP** behauptet.

## Persistenz, Limits und Schutz

- Neuer Einsatzstandard CasinoMaximumStakes.Spitze = 100000 virtuelle €; erlaubt sind 50 bis 1000000, in 50er-Schritten. Der Wert gilt nur für neue Runden.
- Jeder akzeptierte bezahlte Spin bucht einmal den Rundeneinsatz. Action Games und Einsammeln kosten nichts; während der Runde bleibt die Einsatzhöhe gesperrt.
- Konservative maximal reservierte Rundenauszahlung: (100 + 7 + 1) × 336 × Einsatz; bei 100000 sind das 3628800000 virtuelle €.
- Die bestehende Kontoobergrenze und mögliche Reservierungen werden vor der Zufallsziehung geprüft. Die Datenbank schützt Wiederholungsanfragen mit Operations-ID und Revision.
- Historische Risk-Runden und die dazugehörigen Regeln, Dateien und IDs dürfen nicht entfernt werden.

## UI und Installation

Die Lobby verwendet aus Kompatibilitätsgründen weiterhin die Button-ID casino_lobby_risk, öffnet damit jedoch Alles Spitze. Das neue Panorama-Layout und die CSS-Walzenanimation liegen isoliert in ui/AnoCore/layout/custom_game/anocore/ano_casino_spitze.xml und ui/AnoCore/styles/custom_game/anocore/ano_casino_spitze.css. Die bestehenden Book- und Risk-Ressourcen bleiben wegen ihrer vorhandenen SHA256-Quell-Sperren bytegenau unverändert. Die fünf am 19.10.2026 bereitgestellten Originalbilder sind im lokalen Testpaket als vorbereitete 320×320-PNG-Texturen enthalten und fest mit der Ergebniswalze verbunden. Der Workshop-Build benötigt dafür weder Python noch ein zusätzliches Bilderverzeichnis.

### Skin-Bilder wie bei Biggest Boy fest ins Workshop-Addon integrieren

Die Originalbilder liegen direkt neben den anderen AnoCasino-Symbolen in `ui/AnoCore/styles/custom_game/anocore/casino/`. Eine zweite Bildablage ist nicht notwendig. Bei einem entpackten Arbeitsverzeichnis `C:\AnoCore-Neu` entspricht das:

`C:\AnoCore-Neu\ui\AnoCore\styles\custom_game\anocore\casino\`

Dorthin gehören:

- `ggspray.png` – Verlustsymbol
- `safarimesh.webp` – niedriger Gewinn
- `neonrevolution.webp` – mittlerer Gewinn
- `gold.webp` – hoher Gewinn
- `bluegem.png` – Messer / Joker

Der normale `build.ps1` und `build-casino.ps1` rufen `ensure-spitze-assets.ps1` automatisch auf. Sind alle fünf Quelldateien vorhanden, werden sie in genau demselben Ordner in `spitze_ggspray.png`, `spitze_safarimesh.png`, `spitze_neonrevolution.png`, `spitze_gold.png`, `spitze_knife.png` umgewandelt. Sind die bereits vorbereiteten `spitze_*.png`-Dateien vorhanden und keine Originaldateien, wird Python nicht benötigt. Liegen nur einzelne Originalbilder vor, gibt es einen klaren Fehler statt unvollständiger Symbolbilder.

Der gemeinsame CS2-Workshop-Build erstellt danach zu den fünf Versionierten `.vtex`-Deskriptoren die tatsächlichen Valve-Texturen `.vtex_c`. Das fertige Workshop-Addon liefert diese kompilierten Bilder **an alle Spieler aus**; niemand muss sie selbst herunterladen.

```powershell
cd "C:\AnoCore-Neu"
powershell -NoProfile -ExecutionPolicy Bypass -File .\ui\AnoCore\build.ps1 -Addon anomeme_ui -RequireSpitzeSkins -InstallLocalClient
```

Der Schalter `-RequireSpitzeSkins` verhindert, dass ein fehlendes Symbol unbemerkt im Workshop landet. CS2 muss danach vollständig neu gestartet werden. Das bisherige Workshop-Addon wird nicht allein durch `-InstallLocalClient` geändert; nach lokaler Abnahme die bestehende `anomeme_ui` mit den kompilierten Ressourcen im Workshop aktualisieren. Bei einem Testserver darf keine andere, veraltete gemountete UI die lokalen Assets überlagern.

Die fünf vorbereiteten `spitze_*.png`-Dateien sind lokal eingecheckt und im Testpaket enthalten. Der Eigentümer hat die Veröffentlichung samt Bildern am 19.10.2026 ausdrücklich freigegeben; die vorbereiteten Bilder sind nun im GitHub-Branch enthalten.

Ein reproduzierbarer Seed-/Strategie-Test prüft die modellierte Brutto-Auszahlungsquote für das feste Szenario «bis zu 100 bezahlte Spins, vorher nur bei Action Games stoppen». In einer 12.000-Runden-Stichprobe ergaben sich etwa 90 % Bruttorücklauf. Das ist eine Simulation der unabhängigen AnoCasino-v1-Regeln, **kein zertifizierter RTP-Wert** und keine Garantie für andere Einsammelstrategien.

Zum lokalen Test sind ein passendes AnoCore-Serverplugin und die aus denselben Quellen neu kompilierte anomeme_ui-Workshop-UI erforderlich; der alte risk.vtex bleibt ausschließlich für gespeicherte Legacy-Runden erhalten. Keine Änderung direkt am Live-Server ohne Abnahme.

Auf einem Testserver mit zwei Clients prüfen: alle sechs Ergebnisarten, sichtbare Walzenbewegung, 15 Stufenlichter, Cashpot, Direkterfolge, Einsammeln, sieben Action Games, 100er-Grenze, 100000-Einsatz, Duplikat/Retry, Reconnect, Legacy Risk, Fehlermeldungen und Client-Panorama-Parsing. Normale .xml/.css-Dateien ersetzen keine Valve-kompilierten UI-Ressourcen. Auch nach grüner CI bleibt die CS2-Abnahme Pflicht.

Referenzen: https://merkur-spielewelt.de/spiele/alle-spiele/alles-spitze.html und https://merkur.com/spiele/beitraege/alles-spitze.html
