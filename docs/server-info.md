# Begrüßungen und automatische Infotexte

`addons/counterstrikesharp/plugins/AnoCore/config/server-info.json` steuert private
Begrüßungen und regelmäßig rotierende Chatnachrichten. Ohne bestehende Datei wird
eine deaktivierte Standardkonfiguration erzeugt. Die fertige
[ANOMEME-Vorlage](../examples/server-profiles/anomeme/server-info.json) übernimmt
Texte, Farben, 10 Sekunden Begrüßungsverzögerung und 180 Sekunden Intervall aus dem
alten Advertisement-Profil. Alte Command-Hinweise wurden auf AnoCore angepasst;
Community-Links stammen aus dem Backup und sollten vom Betreiber geprüft werden.

## Installation

1. Aktuellen Plugin-Build installieren und Server neu starten.
2. `examples/server-profiles/anomeme/server-info.json` aus dem Entwicklungspaket
   nach `addons/counterstrikesharp/plugins/AnoCore/config/server-info.json` kopieren.
   Bei vorhandener Datei erst die eigenen Texte sichern.
3. In der **Serverkonsole** `css_anoreloadconfig server-info` ausführen.
4. Neu verbinden und nach 10 Sekunden die Begrüßung prüfen. Nach 180 Sekunden
   erscheint der nächste Infotext. Ein Workshop-Update ist nicht erforderlich.

`Enabled` schaltet beide Arten von Nachrichten ein/aus. `WelcomeDelaySeconds`
(0–120) gilt ab Verbindungsbeginn. `WelcomeMessages` enthält bis zu 12 einzelne
Zeilen, die ausschließlich an die aktuelle Sitzung des Spielers gehen.
`InfoMessages` enthält bis zu 32 Blöcke mit je bis zu 8 Zeilen. Alle verbundenen
Spieler erhalten einen Block pro `IntervalSeconds` (30–86400), in Reihenfolge.
Leere Listen deaktivieren die jeweilige Ausgabe. Nach Pausen gibt es keine
nachträgliche Nachrichtenflut. Ein Reload startet das Intervall und die Rotation
neu, ohne bereits begrüßte Sitzungen erneut zu begrüßen. Aktivierung oder
Plugin-Neustart kann bereits verbundene Spieler einmal begrüßen.

## Texte und Farben

```json
{
  "Enabled": true,
  "WelcomeDelaySeconds": 10,
  "IntervalSeconds": 180,
  "WelcomeMessages": [
    "{Green}Willkommen {Red}{player.name}{Green}!",
    "{Green}Aktuelle Map: {Red}{map.name}",
    "{Silver}Deine Spielzeit: {Yellow}{playtime.total}"
  ],
  "InfoMessages": [
    ["{Green}Unser TeamSpeak: {Red}anomeme.com"],
    ["{Green}Menü öffnen: {Yellow}!anomenu"]
  ]
}
```

| Platzhalter | Inhalt |
| --- | --- |
| `{player.name}` | Bereinigter Spielername, bis zu 64 Zeichen |
| `{map.name}` | Aktuelle native Map, bis zu 128 Zeichen |
| `{playtime.total}` | Gespeicherte Gesamtspielzeit, z. B. `20d 8h 59m` |

Farbnamen sind unabhängig von Groß-/Kleinschreibung: `Default`, `White`,
`DarkRed`, `LightPurple`, `Green`, `Olive`, `Lime`, `Red`, `Grey`, `Yellow`,
`Silver`, `LightBlue`, `DarkBlue`, `Purple`, `LightRed`, `Orange`.
Jede Zeile endet mit einem Farbreset. Keine Roh-Steuerzeichen oder Zeilenumbrüche
innerhalb einer Zeile verwenden; für mehrere Zeilen mehrere Listeneinträge anlegen.
Unbekannte Platzhalter, zu lange Texte (mehr als 240 Zeichen bzw. expandiertes
Ausgabelimit) und ungültige Intervalle werden abgelehnt. Ein fehlgeschlagener
Reload lässt die vorherige gültige Konfiguration aktiv.

Spielzeit wird nur abgefragt, wenn die ausgewählten Texte den Platzhalter nutzen.
Es handelt sich um den letzten dauerhaft gespeicherten Stand, keinen zusätzlichen
Zähler. Bei Datenbankfehlern steht dort `nicht verfügbar`; die übrigen Hinweise
bleiben nutzbar. Die bestehende periodische Playtime-Benachrichtigung ist unabhängig
und kann in `playtime-notifications.json` deaktiviert werden, falls sie doppelt ist.

## Abnahme

Automatisierte Tests prüfen Zeitsteuerung, Rotation, Farben, sichere Platzhalter,
Reload und Schutz vor Nachrichten an ersetzte Sitzungen. Auf CS2 zusätzlich Farben,
Zeilenreihenfolge, zwei Empfänger, Mapwechsel, Disconnect vor der Begrüßung,
Deaktivieren/Reload sowie Hotreload ohne doppelte Timer prüfen. Native Ausgabe
bleibt eine manuelle Serverprüfung.
