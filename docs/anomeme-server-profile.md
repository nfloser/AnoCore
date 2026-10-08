# ANOMEME: Serverregeln und Progression konfigurieren

Das Profil unter `examples/server-profiles/anomeme/` übernimmt die **currentValue**-
Werte aus dem bereitgestellten Server-Backup vom 08.10.2026 (Zenith Ranks/Stats
und `ranks.jsonc`). Es enthält keine Zugangsdaten oder Spielerdaten. Es ist ein
Vorschlag für AnoCore-Konfigurationsdateien und wird nicht automatisch installiert.

## Eure Rangpunkte bleiben eure Rangpunkte

| Aktion / Regel | Rangwert aus dem Backup |
| --- | ---: |
| Neuer Spieler | 1000 Startpunkte |
| Kill / Assist / Tod | +2 / +1 / −2 |
| Headshot / No-Scope | jeweils +1 zusätzlich |
| Knife / Taser / Impact-Kill | +3 / +5 / +10 zusätzlich zum Kill |
| Flash-Assist | +1 zusätzlich zum normalen Assist |
| Rundensieg / Niederlage | +2 / −2 |
| MVP / Plant / Defuse / Bombenexplosion | jeweils +1 |
| Andere CTs bei Defuse / Hostage Rescue / alle Hostages gerettet | jeweils +1 |
| Teamkill / Teamkill-Assist / zusätzlicher Flash-Teamkill-Assist | −6 / −3 / −2 |
| Suizid | −5 |
| Killstreak 3 / 4 / 5 | +1 / +1 / +3 |
| Andere Killstreaks | 0 |
| Smoke / Blind / Penetration / Grenade / Inferno / Distanz-Bonus | 0 |
| Bomben-Pickup/-Drop / Hostage Hurt/-Kill | 0 |
| Warmup / Bots / FFA / dynamische Multiplikatoren | aus |
| VIP-Multiplikator | 1 (kein Bonus) |
| Mindestzahl menschlicher Spieler für Ränge und Statistiken | 8 |
| Playtime-Intervall | 0 (aus); vorhandener Punktewert +1 bleibt konfiguriert |
| Scoreboard | Competitive-Ränge an, Score-Synchronisierung aus |

Der Backup-Punkt `Domination` bezeichnet den Vierer-Killstreak (+1), nicht das
separate native DominatedKill-Event. `SecondsBetweenKills: 0` bedeutet keine
Zeitgrenze innerhalb derselben Runde. AnoCore unterstützt dafür jetzt
`StreakWindowSeconds: 0`; Tod, Sitzungswechsel und neue Runde setzen weiter zurück.
Bewusst konfigurierte Nullen werden nicht durch vermeintlich bessere Boni ersetzt.
Die additive Bonus-/Assist-/Suizid-/Streak-Semantik wurde zusätzlich mit
[Zenith Ranks Events.cs](https://github.com/K4ryuu/K4-Zenith/blob/main/modules/ranks/Events.cs)
abgeglichen (Referenz-Commit `94cd8fb34ffa2121d07ec7846dbcb9c1ac37935e`).

Die 18 Rangnamen/Grenzen werden übernommen: STARTER 0, HARMLESS BOT 1200,
EASY BOT 1400, NORMAL BOT 1600, HARD BOT 1800, EXPERT BOT 2000, HEIZER 2300,
MACHER 2700, DRÜCKER 3200, EIERLEGER 3800, INTERNPRO 4500, INTERNBOSS 5300,
INTERNKING 6200, INTERNGOTT 7200, LEBENDE LEGENDE 8500, LIFEGAMER 10000,
GOTTKÖNIG 12000 und GOAT 15000. Tags verwenden diese Namen. Zenith-spezifische
Rangbilder, Beispiel-Permissions und individuelle Rangfarben werden hier nicht
als neue AnoCore-Berechtigungen oder Layoutregeln erfunden.

**Punktestände sind eine andere Aufgabe als Regeln:** Das Profil nutzt EventLedger,
um eure aktionsbezogenen Boni/Strafen korrekt abzubilden. Es importiert weder
Zenith-Spielerstände noch schreibt es AnoCore-Historie um. Ein bestehendes AnoCore
mit DerivedStatistics darf erst nach dokumentierter Punkteübernahme auf EventLedger
wechseln; ansonsten zeigt der neue Modus andere Punktestände. Vorhandene Werte
lassen sich über die autorisierten Rang-Administrationsbefehle explizit übertragen.
`anosetrankpoints` setzt dabei einen **Adjustment**, nicht einen absoluten historischen
Spielerstand: den bestehenden Ledger-/Basisscore zuerst berücksichtigen.
Das ist keine automatische Migration aus dem hochgeladenen SQL-Backup.

## Zusätzliche Level-XP: getrennte Vorschläge

Das Backup hat keine entsprechende unabhängige Level-Progression. Die neuen
XP-Vorschläge orientieren sich an positiven Rangwerten ×5: Kill 10, Assist 5,
Headshot/No-Scope 5, Rundensieg 10, MVP/Objectives 5, Knife 15, Taser 25 und
Impact 50. Bewusst deaktivierte Spezial-Boni bleiben auch hier 0. Hinzu kommen
FirstBlood 5, MatchWon 50 und MatchLost 10 als neue Vorschläge. Negative Rangwerte
werden nicht zu negativer Lifetime-XP: Niederlagen/Tode kosten keine Level-XP.
Der zusätzliche FirstBlood-/Match-XP-Bonus ändert keine Rangpunkte.

50 Level verwenden die Kurve `500*n + 50*n*(n-1)` mit `n = Level-1`: Level 2
500 XP, Level 3 1100, Level 10 8100 und Level 50 142100. Das ist ein änderbarer
Vorschlag, keine nachträgliche Umwertung vorhandener XP. Beim Vorbereiten mit
bestehender Konfiguration wird die bestehende Kurve übernommen.

Daily: drei Rundensiege → 50 XP, ein MVP → 25 XP. Weekly: zehn AK-Headshots →
150 XP, fünf AWP-NoScopes → 200 XP, fünf AWP-NoScope-Smoke-Kills auf Mirage →
250 XP, zehn Wallbangs → 150 XP, zehn Kills ab 30 Metern → 100 XP, 500 Utility-
Schaden → 100 XP, zehn Plants → 100 XP. Neue IDs beginnen mit `anomeme.`;
bestehende Definitionen/Replays behalten ihre Identität. Permanent gibt es
Headshot-/Rundensieg-/Plant-/Knife-Tiers. Seasons bleiben deaktiviert, bis ihr
bewusst einen Zeitraum festlegt. Es gibt keinen Battlepass.

## Dateien vorbereiten und anwenden

Ziel auf DatHost:
`game/csgo/addons/counterstrikesharp/plugins/AnoCore/config/`.

1. Aktuelle AnoCore-Konfigurationsdateien herunterladen und sichern. Datenbank
   ebenfalls sichern, bevor ihr neue DLLs/Migrationen auf dem Testserver verwendet.
2. Im heruntergeladenen Repository oder Entwicklungs-Paket das Profil offline
   vorbereiten. Für eine bestehende AnoCore-Installation (Windows/PowerShell):

   ```powershell
   py tools/prepare_anomeme_profile.py --existing-config C:\AnoCore-config-alt --output C:\AnoCore-config-vorschlag
   ```

   Für einen neuen Server ohne AnoCore-Konfiguration:

   ```powershell
   py tools/prepare_anomeme_profile.py --output C:\AnoCore-config-vorschlag
   ```

   Unter Linux/macOS `python3` statt `py` verwenden. Das Ausgabe-Verzeichnis darf
   noch nicht existieren. Das Skript verändert keine Eingabedatei und keinen Server.
3. Unterschiede prüfen. Vorhandene gameplay-xp-Werte und EarnFromUtc bleiben,
   neue fehlende Event-Werte werden ergänzt. Vorhandene progression.json gewinnt;
   sonst werden alte Levels/Boosts aus achievements.json übernommen. Bestehende
   Achievement-/Challenge-IDs bleiben unverändert, neue Beispiele werden ergänzt.
   Vorhandene Seasons bleiben. Ränge und Statistik-Eligibility entsprechen bewusst
   dem Backup-Profil. Nicht mehr als 32 Recurring/96 Predefined/32 Achievements.
4. Nur geprüfte JSON-Dateien in AnoCore/config übertragen. **core.json**, DB-String,
   CSS-Konfiguration, MultiAddonManager/Mount-Einstellungen und MatchZy-Dateien
   werden von diesem Profil nicht angefasst. Die rohe gameplay-xp-Vorlage nie
   direkt kopieren: Das Skript setzt für Neuinstallationen einmalig EarnFromUtc;
   bei bestehenden Installationen erhält es die alte Grenze.
5. AnoCore/Server neu starten. `progression`, Definitionen, Ränge und Statistik-
   Eligibility sind restart-only. Startprotokoll auf Validierungsfehler prüfen;
   ungültige Konfigurationen werden nicht als funktionierende Module aktiviert.
   Reguläre gameplay-xp-Belohnungen lassen sich danach über
   `css_anoreloadconfig gameplay-xp` laden; Enable/Checkpoint/EarnFromUtc brauchen
   weiterhin Neustart. **Kein Workshop-Update für Inhalte oder Regeln.**

Das Vorbereitungsskript prüft JSON-Struktur, ID-Merge-Grenzen und Schutzregeln;
die vollständige fachliche Typ-/Definitionsvalidierung übernimmt AnoCore beim
Start. Die ausgelieferten Vorlagen werden zusätzlich in .NET/CI validiert.

## Häufige Änderungen auf dem Server

- Jedes Wochenende Double XP: `gameplay-xp.json` → WeekendMultiplier 2 (UTC
  Samstag/Sonntag). Bei Überschneidung mit einem Event gewinnt der höchste Wert.
- XP-Zeitraum und Label: `progression.json` → Boosts, siehe Beispiel in
  [progression.md](progression.md). Gameplay=1, ChallengeReward=2,
  AchievementReward=4; Quellenmasken durch Addition kombinieren.
- Levelgrenzen: `progression.json` → Levels; alle Level 1..N und streng steigende
  MinimumXp-Grenzen, Level 1 beginnt mit 0. Bestehende XP bleiben unverändert.
- Challenge: Recurring für Daily (WindowKind 0) und Weekly (1), Predefined für
  datierte/Season-Fenster (2). Namen/Ziele/RewardXp/PrerequisiteIds/Predicates sind
  serverseitig. Beispiele in [progression.md](progression.md).
- Achievement: `achievements.json` → Name/Statistic/Tiers/Prerequisites. Bestehende
  IDs/Tiers nicht für eine zweite Auszahlung wiederverwenden; neue Inhalte neue ID.
- Season: `seasons.json` → Enabled true und Seasons ergänzen, etwa:

  ```json
  {
    "Id": "anomeme-season-1",
    "Version": 1,
    "Name": "ANOMEME Season 1",
    "StartsAtUtc": "2026-11-01T00:00:00+00:00",
    "EndsAtUtc": "2027-02-01T00:00:00+00:00"
  }
  ```

  Eine datierte Season-Challenge braucht ihr passendes StartsAtUtc/EndsAtUtc-
  Fenster in Predefined; es gibt keine automatische Verknüpfung durch den Namen.
  Bereits akzeptierte Season-ID/Version/Zeitgrenzen sind unveränderlich. Für
  andere Zeiträume eine neue Version/Definition nach dem Season-Vertrag verwenden.
- Rangname/Grenze/Punkte: `ranks.json`, Neustart. Rang und Level bleiben getrennt.

## Testserver-Abnahme

Mit acht menschlichen Spielern außerhalb des Warmups eine normale Kill-/Assist-
Sequenz, Headshot, NoScope, Knife, Rundensieg/-verlust, Teamkill und Suizid prüfen.
Erwartete Rangwerte aus der Tabelle, separate XP und genau eine Reward-Auszahlung
prüfen. Statistikwerte und kombinierte Missionsbedingungen müssen nach Commit
passen. Danach Reconnect/Neustart und dieselben Werte kontrollieren. Mit sieben
Spielern/Warmup werden neue Rang-/Statistikpunkte nicht erfasst. Die Ausnahme
historischer Kontextdaten und die manuelle DatHost-Abnahme bleiben dokumentiert.
