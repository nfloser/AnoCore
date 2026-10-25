# Casino-Einstellungen auf dem DatHost-Server

**Status:** PR zu #549, Stufe 1 (Biggest Boy + Book). Erst nach geprüftem
Plugin- und passendem Workshop-UI-Build auf dem Testserver installieren.
Bestehende Runden, Presets, Guthaben und Konfigurationen nicht überschreiben.

## Wo einstellen?

`game/csgo/addons/counterstrikesharp/plugins/AnoCore/config/core.json`

Unter `BiggestBoyOutcomes` bzw. `BookOutcomes` kann optional ein
`Paytable`-Block hinzugefügt werden. Nicht vorhandene Werte behalten ihre
bisherigen Defaults. Das folgende JSON ist **ein Ausschnitt**, kein Ersatz
für die vorhandene `core.json` mit Datenbankverbindung und weiteren Feldern:

```json
{
  "CasinoEnabled": true,
  "BiggestBoyMaximumStake": 100000,
  "CasinoMaximumStakes": {
    "Slots": 500,
    "Coinflip": 500,
    "Roulette": 500,
    "Blackjack": 100000,
    "Spitze": 100000,
    "Book": 100000
  },
  "BiggestBoyOutcomes": {
    "WinPercent": 15,
    "FreeSpinPercent": 8,
    "FullScreenPercent": 1,
    "Paytable": {
      "LineStakeDivisor": 10,
      "Multipliers": [
        [10, 50, 200],
        [4, 12, 40],
        [3, 10, 30],
        [2, 8, 25],
        [2, 6, 20],
        [1, 5, 15],
        [1, 3, 10],
        [1, 3, 10],
        [1, 2, 8],
        [1, 2, 8]
      ]
    }
  },
  "BookOutcomes": {
    "WinPercent": 20,
    "FreeSpinPercent": 5,
    "FullScreenPercent": 1,
    "Paytable": {
      "LineStakeDivisor": 10,
      "Multipliers": [
        [2, 5, 10],
        [1, 4, 8],
        [2, 5, 10],
        [3, 8, 15],
        [4, 10, 20],
        [5, 15, 30],
        [8, 25, 50]
      ]
    }
  }
}
```

## So rechnet der Automat

`Liniengewinn = Gesamteinsatz / LineStakeDivisor × Multipliers[Symbol][3/4/5]`

- Für Biggest Boy sind die zehn Zeilen in dieser Reihenfolge:
  **Döner, Bier, Zigarette, Butter, Shot, Trichter, G, O, A, T**.
- Für Book sind es sieben: **Kirsche, Zitrone, Pflaume, Orange,
  Traube, Glocke, Sieben**.
- Pro Zeile stehen genau drei positive **Brutto**-Faktoren für
  **3, 4, 5** gleiche Symbole von links auf einer Gewinnlinie.
- Bei zwei gewinnenden Linien addieren sich beide Linien; jede einzelne
  Linie zahlt höchstens eine Kombination.
- `LineStakeDivisor` ist **1, 2, 5, 10, 25 oder 50**. Standard ist 10.
  Bei 1 werden Gewinne gegenüber 10 zehnmal so hoch, wenn die Faktoren
  unverändert bleiben. Zum Ändern nur des Teilers genügt
  `"Paytable": {"LineStakeDivisor": 1}`.
- `Multipliers` darf vollständig weggelassen werden, dann gelten die
  vorhandenen Standardfaktoren. Wenn vorhanden, müssen es genau 10 bzw.
  7 Zeilen mit jeweils 3 Ganzzahlen von **1 bis 10000** sein.
- Das konfigurierte Profil `BiggestBoyProfile` / `BiggestBoyLuckPercent`
  und die bisherigen drei Prozentwerte bleiben unabhängig einstellbar.

Beispiel: 100 € virtueller Einsatz, fünf Döner in **einer** Linie:
mit Standardfaktor 50 und Teiler 10 = 500 €; mit Teiler 1 = 5.000 €.
Mit dem oben gezeigten angepassten Dönerfaktor 200 bei Teiler 10 =
2.000 €. Ein Vollbild gewinnt auf allen zehn Linien.

## Reservierung und Kompatibilität

Die maximal reservierte Auszahlung wird aus dem höchsten konfigurierten
Faktor, dem Teiler, **zehn Linien** und höchstens **31 Spins** berechnet.
Ein ungünstig hoher Wert kann deshalb bereits beim Start an der
Guthaben-/Reservierungsgrenze abgewiesen werden. Für jede Kombination
die tatsächliche Rückzahlungsquote/RTP prüfen; eine höhere Paytable
ändert die langfristige Bilanz erheblich, auch bei unveränderten
`WinPercent`-/`FreeSpinPercent`-Werten.

Eine neue Runde speichert ihre Prozentwerte **und ihre Paytable**
im Rundenstatus. Freispiele verwenden nach Config-Änderung, Reconnect
und Neustart weiterhin diesen eingefrorenen Status.
Alte kontrollierte v5-/v2-Runden und historische Versionen behalten
ihre alten Auszahlungstabellen. Die native Gewinnplananzeige wird
serverseitig aktualisiert, benötigt aber **einmalig** den neuen
Workshop-Panorama-Build mit den neuen Label-IDs.

Ungültige Paytables werden beim Start der AnoCore-Konfiguration abgelehnt.
Nach Änderung der Datei ist ein kontrollierter Plugin-/Server-Neustart
erforderlich. Kein Live-Hot-Reload und keine Änderungen bereits gebuchter
Runden.

## Weitere Casino-Spiele

Bereits einstellbar in `core.json`: `CasinoMaximumStakes` für Slots,
Coinflip, Roulette, Blackjack, Spitze und Book; zusätzlich
`BiggestBoyMaximumStake`, beide kontrollierten Slot-Wahrscheinlichkeiten
und die Biggest-Boy-Profile. Die Gewinn- und Ereignistabellen von
Slots, Coinflip, Roulette, Blackjack und Alles Spitze sind **in dieser
ersten PR noch nicht freigeschaltet**. Deren Anpassung folgt in
separaten Schritten unter #549, jeweils mit eigener Zustandsversionierung,
Guthaben-Grenzen, UI und Tests. Keine unkontrollierte Einstellung für
beliebige Spieler- oder Wallet-Manipulation.

## Abnahme vor Live-Betrieb

1. Aktuelle Server-Konfiguration und Datenbank separat sichern.
2. Neue DLLs **und** passenden kompletten Panorama-Workshop-Build auf
   einem Testserver installieren.
3. Bei jeder Slot-Version eine Standard- und eine konfigurierte
   Symbolkombination auf allen zehn Linien testen.
4. Buchung, Reservierung, RTP, Reconnect, Neustart und fortgesetzte
   Freispiele nach erneutem Config-Wechsel prüfen.
5. Ungültigen Faktor/Teiler testen: Start muss verständlich scheitern,
   nicht still die falsche Paytable nutzen.
