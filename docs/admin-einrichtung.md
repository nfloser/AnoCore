# AnoCore-Admin einrichten

CounterStrikeSharp-Gruppen wie `#css/admin` und CSS-Flags wie `@css/root` sind unabhängig von den dauerhaft gespeicherten AnoCore-Rollen. Ein CSS-Admin erhält dadurch allein keine AnoCore-Menürechte.

Nach Installation des Builds mit `anosetupadmin` diesen Befehl in der **DatHost-Serverkonsole** ausführen:

```text
css_anosetupadmin 76561198415563775
css_anoroles 76561198415563775
```

Der erste Befehl legt `anocore_admin` mit Immunität 400 an und weist sie dauerhaft zu. Definition, Zuweisung und Audit werden gemeinsam gespeichert; die lokale Autorisierung wird anschließend aktualisiert. Wiederholen ist erlaubt. Andere Rollen und individuelle Regeln bleiben erhalten. Bestehende Deny-Regeln können weiterhin einzelne Aktionen blockieren.

Die Rolle erlaubt `ano.admin.*` und `ano.tournament.*`: Admin-Menü, administrative Spieleraktionen und Turnierverwaltung. Sie vergibt kein allgemeines `ano.*`, keine CSS-Flags und keine Rank-/XP-Verwaltungsrechte. AnoVeto prüft seine CSS-Verwaltungsrechte weiterhin separat.

Wenn `anocore_admin` bereits mit einer anderen Definition existiert, bricht die Einrichtung ab. Prüfe die vorhandene Definition bewusst; der Befehl erweitert nicht stillschweigend die Rechte anderer bereits zugewiesener Spieler.

Im Spiel öffnet `!anoadminmenu` das Admin-Menü. Die aktuelle gemeinsame Turnierverwaltung verwendet unter anderem:

```text
css_anotournamenteventload
css_anotournamenteventstatus
css_anotournamenteventstandings
```

Der Import liest `config/tournament-event.json` mit `Enabled=true`. Bestehende Serverkonfiguration beim Plugin-Update erhalten. Die grafische Turnieransicht wird separat integriert.

Entfernen der AnoCore-Adminrolle:

```text
css_anorevokerole 76561198415563775 anocore_admin 0
```

Abnahme auf dem Testserver: Einrichtung über Serverkonsole, Menü als zugewiesener Spieler öffnen, Turnierimport durchführen, Rolle widerrufen und Zugriff erneut prüfen. Ein normaler Spieler darf `!anosetupadmin` nicht ausführen; CSS-Rollen allein dürfen keine AnoCore-Rechte verleihen.
