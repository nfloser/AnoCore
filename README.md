# AnoCore

AnoCore ist ein modulares CS2-Serverplugin für Community-Server, regelmäßige Spielrunden
und organisierte Matches. Es verbindet Spielerstatistiken, ein separates Rangsystem,
Level und Challenges, Moderation sowie Map-Abstimmungen und Turnierverwaltung.

![ANOMEME-Dashboard – Designvorschau mit Beispieldaten](docs/images/dashboard-example.png)

*Beispielbild einer Designvorschau, kein Ingame-Screenshot. Spielerwerte sind fiktiv.
Schrift, Abstände und Navigation können vom aktuellen CS2-Stand abweichen;
die aktuelle Version trennt Back von Prev/Next page. Das Dashboard benötigt einen Build mit Dashboard-Anbindung.*

## Was kannst du damit machen?

| Bereich | Möglichkeiten |
| --- | --- |
| Spielerprofil | Persönliche Statistiken, Kills/Deaths/Assists, Spielzeit und Rangwerte ansehen |
| Ränge und Bewertung | Rangpunkte und Ranglisten führen; mit AnoRating verbundene Spieler vergleichen, um Teams manuell zusammenzustellen |
| Fortschritt | Level und XP, permanente Achievements, zeitlich definierte Challenges und geplante XP-Boosts; getrennt von Wettbewerbsrangpunkten |
| Seasons | Aktuelle Season, Season-Rangliste und vergangene Seasons ansehen |
| Community | Persönliche Benachrichtigungen einstellen und berechtigte Chat-Tags auswählen |
| Map-Abstimmung | Abstimmungen starten und über das native Panorama-Veto-Fenster bedienen |
| Turniere | Zwei Teams mit SteamID-Rostern und Captains konfigurieren; BO1/BO3/BO5, Ready-Phase, Map-Serie, Messerrundenentscheidung, Seitenwahl, Pause und Serienstand verwalten |
| Administration | Berechtigungsgesteuerte Spieler-/Serveraktionen, Rollen, Moderation, Warnungen und Audit-Protokolle |
| Schnittstellen | Module über das SDK erweitern; optionale authentifizierte Management-Anbindung und konfigurierbare Moderations-Webhooks nutzen |

Typische Nutzung: Ein Community-Server speichert gemeinsame Stats und langfristigen Fortschritt.
Für wöchentliche Spielabende lassen sich Challenges und XP-Boosts konfigurieren.
Bei organisierten Matches hinterlegt der Betreiber Teams und steuert den Matchablauf.
AnoRating unterstützt dabei die manuelle Teamaufteilung; es ist kein automatischer Teambalancer.
Leetify ist eine optionale externe Ergänzung mit eigener API-Konfiguration.

**Entwicklungs- und Teststand:** Automatisierte Tests ersetzen keinen CS2-Servertest.
Turnieraktionen wie Messergewinner und Map-Sieger werden über die dokumentierten Befehle gemeldet;
eine vollständige automatische Turnierplattform wird hier nicht versprochen. Funktionen und Menüpunkte
hängen von aktivierten Modulen, Berechtigungen und dem installierten Build ab. Kein Battlepass.
Siehe [Abnahmematrix](docs/functional-acceptance.md) und [Serverprüfungen](docs/full-system-test.md).

## Installieren

1. [Quick Installation Guide: Windows und DatHost](docs/quick-install.de.md)
2. [Server und Datenbank einrichten](docs/server-setup.de.md)
3. [Steam-Addon mit Logo bauen und hochladen](docs/steam-addon.de.md)
4. [Addon und MultiAddonManager verbinden](docs/server-setup.de.md#addon-und-multiaddonmanager)

Das Serverplugin liefert Logik und persönliche Daten. Das Workshop-Addon liefert die Oberfläche.
MultiAddonManager kümmert sich um die Bereitstellung und das Mounten des Addons.
Für das native Dashboard müssen Plugin und UI zusammenpassen.

## Die wichtigsten Befehle

Im Spielchat mit `!` eingeben. Adminbefehle erfordern passende Rechte.

| Befehl | Funktion |
| --- | --- |
| `!anomenu` | Spieleroberfläche öffnen; Dashboard im dafür integrierten Build |
| `!anocommands` | Tatsächlich registrierte Befehle und deren Verwendung anzeigen |
| `!anostatsmenu` / `!anoranks` | Statistiken beziehungsweise Rangansicht |
| `!anoprogression` / `!anolevel` / `!anoxp` | Fortschritt, Level und XP |
| `!anochallenges` / `!anoachievements` | Challenges und Achievements |
| `!anoseason` / `!anoseasons` / `!anoseasontopcurrent` | Season, Historie und aktuelle Season-Rangliste |
| `!anorating` | Bewertung der verbundenen Spieler |
| `!anosettingsmenu` / `!anochatmenu` | Persönliche Einstellungen und Chat-Tags |
| `!anoveto create` / `!anoveto` | Map-Abstimmung starten beziehungsweise erneut öffnen |
| `!anotournamentstatus` / `!anoready` | Matchstatus beziehungsweise Bereitschaft melden |
| `!anostatus` | Pluginstart und Modulstatus prüfen |

Weitere Details: [Turniere](docs/tournament.md), [Challenges und XP](docs/progression.md),
[Moderation](docs/moderation-commands.md), [Konfigurations-Reload](docs/configuration-reload.md),
[Management-Schnittstelle](docs/management-api.md) und [Modul-SDK](docs/module-sdk.md).

## Entwicklung

.NET 10 SDK; CounterStrikeSharp API 374 oder neuer mit kompatiblem .NET-10-Host.
Build-Abhängigkeit: CounterStrikeSharp.API 1.0.376.

```bash
dotnet restore AnoCore.sln
dotnet build AnoCore.sln -c Release --no-restore
dotnet test AnoCore.sln -c Release --no-build
dotnet format AnoCore.sln --verify-no-changes --no-restore
```

[Architektur](docs/architecture.md) · [Mitwirken](CONTRIBUTING.md) · [Projektregeln](AGENTS.md) · [Deployment](docs/deployment.md)

## Lizenz

GNU GPL v3.0. Copyright- und Herkunftshinweise stehen in [NOTICE.md](NOTICE.md) und [LICENSE.md](LICENSE.md).
