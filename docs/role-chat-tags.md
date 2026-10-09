# Rollen und Custom-Tags

AnoCore liest für Chatpräfixe die **tatsächlich zugewiesenen CounterStrikeSharp-Gruppen**.
K4-Zenith wird dafür nicht benötigt. Die Anzeige verändert keine Gruppen, Flags,
Immunitäten oder AnoCore-Berechtigungen.

Für farbige Begrüßungen und automatisch gesendete Hinweise siehe
[Server-Infotexte](server-info.md); diese sind unabhängig von Rollenpräfixen.

## Native Farbausgabe (#361)

Die native Chat-Ausgabe setzt ein führendes Leerzeichen vor die bereits formatierte
Zeile, entsprechend der Konvention in K4-Zenith (`src/Core/Events.cs`). Farbzeichen
und der Reset nach dem Rollenpräfix bleiben erhalten. Die Bereinigung von
Spielernamen und Nachrichten erfolgt weiterhin vor dieser Ausgabe.

Server-Abnahme nach Austausch des Plugin-Builds und Neustart: normale Nachrichten
im öffentlichen und Teamchat senden. `[DEV]` soll gelb, `[HOST]` lila,
`[ANOMEME]` grün und der persönliche `[FOUNDER]` rot erscheinen. Namen und
Nachricht bleiben bei `NameColor`/`MessageColor: None` unverändert. Teamchat darf
nur die bisherigen Empfänger erreichen; Befehle und Gag-Verhalten ebenfalls
prüfen. Unit-Tests prüfen die Ausgabezeichen, nicht das Rendering im CS2-Client.

## Dateien vom alten auf den neuen Server

Die Pfade sind jeweils relativ zum CS2-Verzeichnis `game/csgo`:

| Vom alten Server | Auf dem neuen Server | Inhalt |
| --- | --- | --- |
| `addons/counterstrikesharp/configs/admins.json` | derselbe Pfad | SteamID-Zuordnungen, Gruppen und direkte CSS-Flags |
| `addons/counterstrikesharp/configs/admin_groups.json` | derselbe Pfad | CSS-Gruppen, Flags und Immunität |
| `addons/counterstrikesharp/configs/admin_overrides.json`, **falls vorhanden** | derselbe Pfad | CSS-Befehlsüberschreibungen; im bereitgestellten Backup liegt nur eine Beispieldatei |
| `cfg/MatchZy/admins.json` | derselbe Pfad, wenn MatchZy weiter genutzt wird | separate MatchZy-Adminliste; auch sie kann Befehlszugriff erlauben |
| `addons/counterstrikesharp/plugins/K4-Zenith-CustomTags/tags.json` | **nicht direkt kopieren** | in die unten beschriebene AnoCore-Konfiguration übertragen |

Vorhandene Zieldateien sichern und Zuordnungen gezielt zusammenführen, wenn sie
bereits andere Admins enthalten. K4-DLLs, Zenith-Modulkonfigurationen und
`predefined_tags.json` werden für diese Funktion nicht benötigt. `core.json`,
DB-Zugangsdaten und MultiAddonManager-Einstellungen gehören nicht zu diesem Schritt.
Nach Änderung der CSS-Dateien ist ein Serverneustart der verlässliche gemeinsame
Anwendungspunkt. Die von CSS geladene Adminliste ist entscheidend, nicht allein
der Inhalt einer noch nicht neu geladenen Datei.

## AnoCore aktivieren

Konfiguration: `addons/counterstrikesharp/plugins/AnoCore/config/role-chat-tags.json`.
Beim ersten Start wird eine deaktivierte Konfiguration erzeugt (`Enabled: false`).
Das geprüfte Beispiel liegt unter
[`examples/server-profiles/anomeme/role-chat-tags.json`](../examples/server-profiles/anomeme/role-chat-tags.json).
Es übernimmt die Gruppen, Texte und Farben des hochgeladenen Backups; Zeniths
`MAGENTA` wird auf den vorhandenen nativen Farbwert `Purple` abgebildet.

Diese optionale Datei **separat** übernehmen und prüfen. Das bestehende Offlinewerkzeug
`tools/prepare_anomeme_profile.py` bereitet weiterhin die sieben Punkte-/Progressionsdateien
vor und importiert weder Adminzuordnungen noch persönliche Tags.

| Zuordnung | Präfix | Farbe | Priorität |
| --- | --- | --- | --- |
| persönliche Anzeigeüberschreibung | z. B. `[FOUNDER]` | z. B. `Red` | vor allen Gruppen |
| `#css/admin` | `[ADMIN]` | `LightRed` | 400 |
| `#css/dev` | `[DEV]` | `Yellow` | 300 |
| `#css/host` | `[HOST]` | `Purple` | 200 |
| `#css/og` | `[OG]` | `DarkBlue` | 100 |
| `#css/normal` | `[ANOMEME]` | `Green` | 0 |
| keine passende Gruppe | `[ANOMEME]` | `Green` | Standard |

Mehrere Gruppen: die höchste Priorität gewinnt. Gruppennamen werden exakt und
unter Beachtung der Groß-/Kleinschreibung verglichen. Root-Flags bedeuten hier
keine zusätzliche Gruppenmitgliedschaft. Gleiche Prioritäten und doppelte Gruppen
werden abgewiesen. Maximal 32 Gruppen und 32 persönliche Überschreibungen, Prioritäten
zwischen -1000 und 1000, Texte mit 1–24 druckbaren Zeichen ohne Steuerzeichen oder
geschweifte Klammern. Farben siehe [Chatformatierung](chat-formatting.md).

Im Backup ist `[FOUNDER]` ein persönlicher Tag, keine eigene CSS-Gruppe. Trage die
zugehörige SteamID64 aus deiner alten privaten `tags.json` als Schlüssel unter
`PlayerOverrides` ein; der Wert ist `{ "Text": "[FOUNDER]", "Color": "Red" }`.
Das öffentliche Beispiel enthält bewusst keine Spieleridentitäten. Alternativ
kannst du eine echte Gruppe wie `#css/founder` in CSS definieren, ihr den Spieler
zuordnen und eine Tagregel mit höherer Priorität konfigurieren. Eine rein optische
Founder-Gruppe braucht keine zusätzlichen Flags.

`DefaultTag: null` unterdrückt das Standardpräfix; `Enabled: false` deaktiviert alle
Rollenpräfixe einschließlich persönlicher Überschreibungen. Alle Änderungen sind
mit `css_anoreloadconfig role-chat-tags` in der Serverkonsole neu ladbar.
Für den Chatbefehl benötigen Spieler `ano.core.reload`. Ungültige Änderungen lassen
die letzte akzeptierte Konfiguration aktiv. Bereits geladene CSS-Gruppenänderungen
wirken beim nächsten gewöhnlichen Chatbeitrag ohne Reconnect; es gibt keinen
zusätzlichen Tag-Cache und keine Datenbankabfrage im nativen Chat-Hook.

Bei abgefangenen Nachrichten steht das Chatkanal-Präfix vor dem Rollenpräfix: `[ALL] [DEV] Name: Text` beziehungsweise `[TEAM] [DEV] Name: Text`. Das native `(TEAM)`-Template-Präfix wird zur Vermeidung doppelter Labels entfernt. Commands werden weiterhin nativ durchgereicht. Das Rollenpräfix steht vor dem übrigen Chatformat. Ränge und selbst gewählte
Tags können weiterhin separat angezeigt werden und überschreiben die Rollenanzeige
nicht. Für die schlichte Anzeige wie auf dem alten Server kannst du in
`chat-format.json` die Vorlagen auf `{player.name}: {message}` und
`(TEAM) {player.name}: {message}` setzen und `css_anoreloadconfig chat-format`
ausführen. Namen/Nachrichten bleiben untrusted Daten: native Farbcodes werden
entfernt, Platzhalter darin nicht ausgewertet. Farben der Rollenpräfixe werden
vor dem restlichen Chatformat zurückgesetzt. Moderation, Teamempfänger und
`!`-/`/`-Command-Weiterleitung bleiben im gemeinsamen Chatpfad.

Nur wenn der gemeinsame Chat-Formatter erfolgreich geladen ist, werden Rollenpräfixe
ausgegeben. Fehler beim Laden dieser optionalen Tagkonfiguration werden isoliert
protokolliert und schalten keine anderen Module ab.

## Rechte für Mapwechsel und Administration

Die Gruppen aus dem Backup enthalten für Host und Dev `@css/map`,
`@css/changemap` und `@css/config`; Admin hat `@css/root`. OG/Normal erhalten
keine solchen Flags. Diese Definitionen beim Übertragen erhalten.
MatchZys `.map` prüft in der referenzierten Implementierung `css_map`/`@css/map`;
zusätzliche MatchZy-Adminlisten, Overrides und ihre Konfiguration können den Zugriff
ebenfalls beeinflussen. Insbesondere `matchzy_everyone_is_admin` darf für diese
Zugriffstrennung nicht aktiviert sein; Einträge ohne Flags in MatchZys eigener
Adminliste erlauben in der referenzierten Version Vollzugriff. Prüfe deshalb Host, Dev/Admin **und** einen normalen Spieler
mit der tatsächlich installierten MatchZy-Version. Das Tagsystem registriert keinen
eigenen `.map`-Befehl und kann diese Fremdplugin-Zugriffe nicht beschränken.

Der gemeinsame Chatpfad reicht die exakt konfigurierten Punktbefehle durch,
anstatt sie mit Tags neu auszugeben. `chat-format.json` enthält dafür
`PassthroughCommands`, standardmäßig `.map`, `.prac`, `.ready`, `.unready`, `.pause`,
`.unpause`, `.stay`, `.switch`. Weitere verwendete MatchZy-Befehle gezielt ergänzen
und `css_anoreloadconfig chat-format` ausführen; Details und Grenzen stehen in
[chat-formatting.md](chat-formatting.md). Das Durchreichen erteilt keine Rechte.

**AnoCore-eigene Befehle verwenden weiterhin das bestehende `ano.*`-Modell.**
CSS-Dateien zu kopieren vergibt beispielsweise noch kein `ano.admin.kick` oder
`ano.core.reload`. Dafür gelten die dokumentierten AnoCore-Rollen-/Grant-Befehle
und deren Audit-/Immunitätsregeln in [authorization.md](authorization.md).
Es gibt keine automatische Übertragung von `@css/root` in `ano.*`-Vollzugriff.
Ein `[ADMIN]`-/`[HOST]`-/`[FOUNDER]`-Tag ist niemals ein Berechtigungsnachweis.

## Native Abnahme

Automatisierte Tests prüfen Prioritäten, persönliche/Standardpräfixe, deaktivierten
Modus, Validierung, atomaren Reload, echte Format-/Teamrouting-Integration und
Lookupfehler. Zusätzlich auf einem Testserver prüfen:

1. Ohne K4-Zenith: Admin/Dev/Host/OG/Normal verbinden und Tags/Farben vergleichen.
2. Zwei Gruppen zuweisen, Founder-Überschreibung ergänzen und wieder entfernen.
3. CSS-Zuordnung tatsächlich neu laden; sofort neuen Chat senden und Entzug prüfen.
4. Teamchat mit zwei Teams: genau eine Zeile, keine gegnerischen Empfänger.
5. Gag/Command-Weiterleitung, Namen mit Steuerzeichen, ungültigen Reload,
   `Enabled: false`, Reconnect, Plugin-Unload und Neustart prüfen.
6. Separat `.map <Workshop-ID>` als Host und als Normal prüfen; AnoCore-Adminbefehle
   ohne/mit explizitem `ano.*`-Grant prüfen. Tagkonfiguration darf Rechte nie ändern.

Die CS2-/DatHost-Abnahme wurde in der Implementierungssitzung nicht durchgeführt.

## Quellen

- [CSS-Gruppen und Zuordnungen](https://docs.cssharp.dev/docs/admin-framework/defining-admin-groups.html)
- [CSS-Flags](https://docs.cssharp.dev/docs/admin-framework/defining-admins.html)
- [CSS AdminGroup: Root-Semantik von PlayerInGroup](https://github.com/roflmuffin/CounterStrikeSharp/blob/main/managed/CounterStrikeSharp.API/Modules/Admin/AdminGroup.cs)
- [MatchZy Mapwechsel](https://github.com/shobhit-pathak/MatchZy/blob/dev/Utility.cs)

## AnoVeto mit denselben Host-/Dev-Gruppen (#352)

Im CS2-Plugin verwenden `!anoveto create` und `!anoveto cancel` jetzt ausschließlich
CSS `@anocore/veto`. Ergänze dieses Flag in den bestehenden `#css/dev`- und
`#css/host`-Gruppen neben `@anocore/team`; vorhandene Flags, Zuordnungen und
Immunität bleiben erhalten. Admin mit `@css/root` erhält den Zugriff über CSS.
OG/Normal bekommen keines dieser Flags. `!anoveto` und die Abstimmung eines
berechtigten Teilnehmers benötigen kein Managementflag.

Die Prüfung erfolgt im Server-Update mit der beim Aufruf erfassten Spielersitzung,
aktuellen CSS-Rechten und aktivem Runtime. Entzug, Reconnect, Unload oder ein
fehlgeschlagener CSS-Lookup verhindern die vorgemerkte Managementoperation.
AnoVeto besitzt dafür einen separaten, auf seinen festen VoteId beschränkten
Vote-Service. Andere Votes und SDK-Integrationen verwenden unverändert
`ano.vote.manage`; es gibt keine globale Berechtigungsbrücke. Temporäre
AnoCore-Workaround-Grants allein autorisieren das native AnoVeto nicht mehr.
CSS-Änderungen tatsächlich laden (verlässlich: Serverneustart); ein Rollenpräfix
allein ist kein Recht. Die Konsole bleibt für AnoVeto wie zuvor ausgeschlossen.

Manuell prüfen: Host, Dev und Root erstellen/abbrechen; OG/Normal werden dabei
abgewiesen, können aber an einer aktiven Abstimmung teilnehmen. Vor Ausführung
entzogene Flags, Reconnect und Plugin-Unload dürfen keine Operation nachholen.
Die CS2-/DatHost-Abnahme bleibt offen und wurde nicht durch Unit-Tests ersetzt.

## Drei passende Dateien offline vorbereiten (#354)

Das neue Werkzeug übernimmt die tatsächlichen Präfixe und Farben der alten
`K4-Zenith-CustomTags/tags.json`, einschließlich privater SteamID-Overrides wie
Founder. Es hängt `@anocore/team` und `@anocore/veto` an die vorhandenen Dev-/Host-
Flags an. Alle anderen Gruppenfelder, Flags und Immunitäten bleiben erhalten.
Es benötigt die **aktuelle** `admin_groups.json` des Zielservers; die
`admins.json` mit Spielerzuordnungen wird weder erzeugt noch verändert.

Im entpackten AnoCore-Paket oder Repository mit installiertem Python ausführen:

```powershell
python tools/prepare_css_profile.py --tags "C:\Serverbackup\addons\counterstrikesharp\plugins\K4-Zenith-CustomTags\tags.json" --groups "C:\Zielserver\admin_groups.json" --output "C:\AnoCore-Rollen"
```

Der Ausgabeordner muss neu sein. Quelldateien werden nicht überschrieben. Das
Werkzeug schreibt keine Datenbankverbindung, `core.json`, Workshop- oder
MultiAddonManager-Konfiguration. Die erzeugte `chat-format.json` zeigt nur den
Rollentag vor dem Namen; zusätzliche Rangtags entfallen damit in der Chatanzeige.
Die üblichen MatchZy-Punktbefehle bleiben durchgereicht. Zusätzliche eigene
`PassthroughCommands` vor dem Installieren übernehmen.

| Erzeugte Datei | Ziel relativ zu `game/csgo` |
| --- | --- |
| `role-chat-tags.json` | `addons/counterstrikesharp/plugins/AnoCore/config/role-chat-tags.json` |
| `chat-format.json` | `addons/counterstrikesharp/plugins/AnoCore/config/chat-format.json` |
| `admin_groups.json` | `addons/counterstrikesharp/configs/admin_groups.json` |

Alle drei Ergebnisse vor dem Kopieren prüfen und vorhandene Zieldateien sichern.
Der private Founder-Eintrag bleibt in der Ausgabe; diese Datei nicht ins öffentliche
Repository hochladen. Nach dem Gruppenwechsel neu starten. Bei späteren reinen
Anzeigeänderungen reichen die getrennten Konsolenbefehle
`css_anoreloadconfig role-chat-tags` und `css_anoreloadconfig chat-format`.

Unterstützt werden die fünf dokumentierten Gruppen, `all`, bis zu 32 private
SteamID-Overrides und genau ein führender nativer Farbplatzhalter pro Präfix.
`MAGENTA` entspricht `Purple`. Nicht unterstützte Selektoren, zusätzliche
Namens-/Nachrichtenfarben, Clantags oder Auswahlkonfigurationen werden abgewiesen,
weil sie eine explizite Migration benötigen. Doppelte JSON-Schlüssel, beschädigte
Flags und fehlende Dev-/Host-Gruppen verhindern jede Ausgabe. JSON-Kommentare und
UTF-8-BOM sind unterstützt; jede Eingabedatei ist auf 1 MiB begrenzt. Fehlerausgaben
enthalten keine privaten Spieleridentitäten.
