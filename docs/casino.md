# AnoCasino – spielbare Version und Bildertausch

**Neues Spiel:** Risk wird in der Lobby durch Alles Spitze ersetzt. Die fünf CS-Symbole,
Cashpot, drei Leitern, Direkterfolge, Action Games, Regeln und native Abnahme stehen
in [alles-spitze.md](alles-spitze.md). Historische Risk-Runden bleiben kompatibel.


> Erweiterungsstatus: Blackjack, Risk und der Book-artige Bonus-Slot werden in
> separaten Regel- und Persistenzpaketen entwickelt. Sie sind erst nach nativer
> Presenter-/Plugin-Integration spielbar. Kistenöffnung und simuliertes Inventar
> gehören ausdrücklich nicht zu AnoCasino.

Diese Version ergänzt den separaten Drei-Frucht-Animationstest. Der bestehende Stand
`anocasino-v1` bleibt der Legacy-Vertrag für gespeicherte Vorgänge; neue
Spiel-Engines dürfen dessen Auswertungen und Belege nicht verändern. Slots, Coinflip
und Roulette sowie Blackjack, Risk und Book-Freispiele sind über die native Lobby angebunden. Die native CS2-Darstellung und alle
Workshop-Ressourcen müssen auf dem Testserver abgenommen werden; Sourcechecks
und CI ersetzen diese Abnahme nicht. Keine Echtgeld-Einzahlung oder Auszahlung.

## Aktivierung

Passendes Plugin und aktualisiertes Workshop-Addon zusammen installieren. In der
bestehenden `config/core.json` die übrigen Werte erhalten und ergänzen:

```json
"PanoramaMenusEnabled": true,
"CasinoEnabled": true,
"ArcadeAnimationProbeEnabled": false
```

Server neu starten. Schema-Migrationen 27/28 erzeugen AP-Konten, Vorgangsledger und dauerhafte Runden;
sie wird vor Aktivierung des Casinos angewendet. Das gemeinsame Konto hängt an
der SteamID64, nicht am Namen, Slot oder an einer CS2-Sitzung. Es ist unabhängig
von XP und Rangpunkten. Ein neues Profil startet mit null AP.

In der Serverkonsole einzeln:

```text
css_anoroledefine casino_player 0 ano.casino.play
css_anograntrole <SteamID64> casino_player 0 Casino-Test
css_anoapcredit <SteamID64> 5000
```

`anoapcredit` ist ausschließlich aus der Serverkonsole erlaubt, nimmt 1 bis
1.000.000 AP an und schreibt eine dauerhafte Ledgerbuchung. Keine automatische
Punktevergabe beim Login. Für andere Spieler nur den Grant wiederholen. Der
Befehl ist unabhängig von CSS admins.json. Vorhandene Rollen vor Neudefinition
prüfen. Das Casino-Konto ist auf insgesamt 1.000.000.000 AP begrenzt. Vor einem Spiel muss genug
Spielraum für die maximal mögliche Auszahlung bestehen; dies wird vor der
Zufallsziehung geprüft. Gewinnauszahlungen werden nicht nachträglich gekappt.

Im Chat `!anocasino` oder `!anomenu` → Open menu → AnoCasino.
`!anoap` zeigt Profilguthaben und reserviertes Automatenguthaben. Das Dashboard
übernimmt diese Anzeige über die registrierte Kontoabfrage.

## Bedienung

- Aufladebetrag mit ± wählen (50–10.000 AP), dann **Aufladen**. Dies ist eine
  Umbuchung vom Profilkonto, keine neue Punktevergabe.
- **Alles zurückbuchen** verschiebt das gesamte Automatenguthaben ins Profil.
- Einsatz mit ± wählen (50–500 AP, Schritte von 50).
- Spiel und Auswahl wählen, dann **Spielen**. Während einer Buchung/Animation
  werden neue Wetten, Umbuchungen und Auswahlwechsel serverseitig verworfen.
- **Verlauf** zeigt die letzten fünf Spiele aus den letzten zwanzig Vorgängen.
- **AnoMenu** schließt das Casino und öffnet das Dashboard. Schließen und
  Überspringen ändern bereits gebuchte Ergebnisse nicht. Nach Reconnect,
  Mapwechsel oder Neustart bleiben Konto, Reservierung und Ledger erhalten.
- **Buchung prüfen** wiederholt bei unbestätigtem Ergebnis dieselbe Vorgangs-ID.
  Kein neues Ergebnis und keine doppelte Abbuchung. Bis zur Klärung sind neue
  Buchungen gesperrt. Nach einem kompletten Client-/Pluginneustart Konto und
  letzten Spielverlauf prüfen, bevor eine neue Wette abgegeben wird.

## Versionierte Spielregeln: anocasino-v1

Die Regeln sind eigene Regeln, keine mathematische Kopie eines kommerziellen
Automaten. Alle genannten Auszahlungen enthalten den zurückgegebenen Einsatz.

### Slots

Drei Walzen, drei sichtbare Reihen, fünf Linien: drei horizontale Reihen und die
beiden Diagonalen. Der Gesamteinsatz wird gleichmäßig auf die fünf Linien
verteilt. Drei gleiche Symbole auf einer Linie gewinnen. Jede der neun Zellen
wird unabhängig mit den folgenden Gewichten gewählt:

| Symbol-ID | Gewicht von 112 | Auszahlung pro Linien-Einsatz |
|---|---:|---:|
| cherry | 30 | 12× |
| lemon | 25 | 22× |
| plum | 20 | 36× |
| orange | 15 | 55× |
| grapes | 10 | 100× |
| watermelon | 7 | 150× |
| bell | 4 | 300× |
| seven | 1 | 1.000× |

Mehrere Linien addieren sich. Kein Wild, Scatter, Jackpot, Freispiele oder
Autospin in diesem Regelstand. Die theoretische langfristige Rückzahlung ist
93,4585259885 %: Summe `(Gewicht / 112)^3 × Multiplikator`. Kein Versprechen für
eine einzelne Sitzung. Walzenanimation zeigt das bereits gebuchte Ergebnis.

### Coinflip

Kopf oder Zahl auswählen. Zwei gleich wahrscheinliche Ergebnisse aus
serverseitiger kryptografischer Zufallsquelle. Richtige Auswahl: 2× Gesamteinsatz;
falsche Auswahl: null. Theoretische Rückzahlung 100 %.

### Roulette

Europäisches Roulette mit einer Null, 37 gleich wahrscheinliche Ergebnisse.
**Eine Wette pro Runde**: direkt eine Zahl 0–36 anklicken oder Rot/Schwarz,
Gerade/Ungerade, 1–18/19–36, eines der drei Dutzende oder eine der drei Spalten.
Zahl: 36×; Dutzend/Spalte: 3×; andere Außenwetten: 2×. Die Null verliert bei
allen Außenwetten. Theoretische Rückzahlung 36/37 ≈ 97,2973 %.
Mehrere gleichzeitige Einsätze sowie Split/Street/Corner/Six-Line-Innenwetten
sind in dieser Oberfläche nicht enthalten. Das Zahlenfeld folgt der üblichen
Rot-/Schwarzzuordnung; es entscheidet kein Ergebnis im Client.

## Bilder austauschen

Acht einzelne transparente PNGs mit passenden VTEX-Deskriptoren:
`ui/AnoCore/styles/custom_game/anocore/arcade/{Symbol-ID}.png`.
Die Symbol-ID beeinflusst Regeln, die PNG nur die Darstellung. PNG unter
identischem Namen ersetzen; quadratisches Canvas, echte Transparenz,
zentriertes Motiv und vergleichbare sichtbare Größe beibehalten. Die neuen
Originalmotive wurden einzeln mit dem eingebauten Imagegen-Werkzeug erzeugt:
Orange, Trauben, Wassermelone, Goldglocke und grüne Sieben, jeweils glossy,
freigestellt, ohne Flammen, Rahmen, Partikel oder äußeren Glow.

Das UI bleibt clean schwarz/grün und verwendet das vorhandene ANOMEME-Banner.
Rahmen, Buttons, Text, Gewinnlinien und Roulette-Zahlen sind native UI-Elemente;
sie benötigen keine neu generierten Bilder. Bei kleinen Auflösungen ist das Fenster in der Höhe begrenzt und vertikal
scrollbar; das muss ebenfalls nativ geprüft werden. Grundlage sind die
[Panorama-Eigenschaften](https://developer.valvesoftware.com/wiki/CSGO_Panorama_CSS_Properties)
und [Scroll-Panel-Dokumentation](https://developer.valvesoftware.com/wiki/Panorama/Overview/Events).
Die Coinflip-/Roulette-Ergebnis-
animation ist derzeit eine native rotierende Ergebnisanzeige, kein fertiges
physikalisches Münz- oder Roulette-Radmodell. Dekorative Kartenbilder können
später ergänzt werden, ohne Buchungen oder Gewinnregeln zu ändern.

Nach PNG-Austausch oder Layoutänderung stets neu kompilieren und veröffentlichen:

```powershell
powershell -ExecutionPolicy Bypass -File .\ui\AnoCore\build.ps1 -Addon anomeme_ui
```

Alternativ separater Build `build-casino.ps1 -Cs2 "<CS2-Pfad>"`, Standardname
`anocasino_ui`. Kein doppeltes Mounten derselben Ressourcen aus zwei Addons.
Die kombinierte Version ist für den ersten Test am einfachsten.

## Datenbank und Fehlersicherheit

Ein Vorgang speichert eindeutige ID, Spieler, unveränderlichen Request inklusive
Regelversion und Ergebnis. Unter einer `FOR UPDATE`-Sperre des AP-Kontos werden
Einsatz, Zufallsergebnis, Auszahlung, Revision und Ledger gemeinsam committed.
Gleiche Vorgangs-ID und gleicher Request geben den gespeicherten Beleg zurück;
ein abweichender Request mit gleicher ID wird abgelehnt. Fehler rollen zurück.
Mehrere Server können dasselbe Konto über dieselbe Datenbank verwenden.

Tabellen: `ano_ap_accounts`, `ano_casino_operations`. Kein direkter Clientzugriff.
Keine Nachbuchung im Animations-Timer. Guthaben bleibt bei Abbruch auf dem Konto;
reservierte Mittel sind nach Wiederöffnung manuell zurückbuchbar. Alte
Ergebnisse behalten ihre gespeicherten Symbol-IDs und Auszahlungswerte auch bei
späteren Grafik-/Regeländerungen. AP-Erwerb über Gameplay und weitere Profil-
ansichten sind eigene Erweiterungen, nicht automatisch aktiv.

## Abnahme auf dem Testserver

Alle drei Spiele mit zwei berechtigten Spielern prüfen. Mehrfachklicks während
Buchung/Animation, Einsatzgrenzen, leeres Konto, unzureichende Reservierung,
Aufladen/Zurückbuchen, Textanzeige, zehn aufeinanderfolgende Spiele, Schließen
während Animation, Berechtigungsentzug, Reconnect, Mapwechsel, Hot Reload und
vollständigen Serverneustart testen. AP dürfen nicht entstehen/verschwinden,
außer durch dokumentierte Spielverluste/-gewinne oder Konsolengutschriften.
Konto nach Neustart und den letzten Beleg mit dem Zustand vor Abbruch vergleichen.

Kistensimulator und Wettzentrale bleiben getrennte Folgearbeiten. Der Book-artige
Bonus-Slot gehört zum Pflichtumfang, ist aber erst nach #429 spielbar.

## Kompatibilität und erweiterte Automaten

Migration 027 speichert Requests und Receipts als JSON mit den Standardoptionen
von `System.Text.Json`. Die Enum-Zahlen, Symbolreihenfolge, Regelversion und
repräsentative Auszahlungs-/JSON-Vektoren sind deshalb als Legacy-Vertrag
Regressionstests abgesichert. Belege bleiben unveränderlich: spätere Regel- oder
Darstellungsänderungen interpretieren abgeschlossene Vorgänge nicht neu.

Die Engines aus #424 (Blackjack), #425 (Risk / Double-or-Bank) und #426
(Book-style bonus slot) sind eine teilweise Erweiterung, keine derzeit spielbaren
Funktionen. Sie gelten erst nach #429 einschließlich Persistenz-, UI- und
CI-Integration als spielbar. Diese Dokumentation behauptet keine native
CS2-Abnahme der erweiterten Automaten.

## Erweiterte Spiele in der Lobby (#437)

Die Lobby öffnet Blackjack, Risk und Book neben den bestehenden drei Spielen.
Guthaben zuerst in der Lobby aufladen. Die neuen Ansichten nutzen denselben
Automatenbetrag und dieselbe Berechtigung `ano.casino.play`. **Zur Lobby** erhält
jede laufende Runde; beim nächsten Öffnen eines erweiterten Spiels wird die
aktive Runde automatisch fortgesetzt. Es gibt pro Spieler höchstens eine aktive
mehrstufige Runde. Einsätze bleiben bis zum Abschluss fix. Karten, verbleibendes
Deck, Bonusraster, Freispiele und Risk-Zwischenstände bleiben auf dem Server.

Blackjack: ein zufällig gemischtes vollständiges Deck, Dealer steht auf Soft 17,
Blackjack 3:2, höchstens zwei Hände, Split-Asse bekommen eine Karte. Hit, Stand,
Double und Split werden anhand der legalen Aktionen angeboten. Double und Split
buchen den zusätzlichen Handeinsatz atomar; die Endauszahlung erfolgt einmal.

Risk: Start reserviert den gewählten Einsatz als Zwischenstand. **Risk** hat
50 % Gewinnchance (Verdoppelung), sonst Verlust; **Bank** bucht den Zwischenstand
zurück. Nach fünf Versuchen bleibt Bank. Sofortiges Bank gibt den Einsatz zurück.

Book: fünf Walzen, drei Reihen, zehn Linien. Buch ist Wild und Scatter; drei
Bücher starten zehn Freispiele. Neue Scatter vergeben bis zu fünf weitere Spins,
mit höchstens 20 gleichzeitig verbleibenden und insgesamt 30 Freispielen. Das
expandierende Symbol bleibt fest. Freispiele kosten keinen zusätzlichen Einsatz;
die gesammelte Auszahlung wird am Rundenende gebucht. Die mathematischen Regeln
sind eigene versionierte Regeln und keine kommerzielle Automatenkopie.

**Buchung prüfen** wiederholt die unveränderte Aktion mit derselben ID. Ein
unbestätigter Vorgang blockiert weitere Spielaktionen. Die Buchungsgrenzen
reservieren auch während einer laufenden Runde den maximalen Auszahlungsraum.
Die Lobby-Verlaufsliste zeigt weiterhin die bisherigen Einzelspiele; die neuen
Runden besitzen ein separates dauerhaftes Aktionsledger. Native Windows-Builds,
Workshop-Auslieferung und Zwei-Client-Abnahme sind noch durchzuführen.

Die konkrete Installation steht in [casino-test-install.md](casino-test-install.md).

## Biggest Boy Bücher

Eigenständige siebte Slot mit 5×5-Feld, elf eigenen Symbolen und Kebap-Wild.
[Gewinnplan, Bonusregeln, mathematische Prüfung und Abnahme](biggest-boy.md).
`BiggestBoyMaximumStake` begrenzt den Einsatz auf bis zu 10.000 AP.

## Währungsanzeige

Alle Casino-Guthaben, Einsätze und Gewinne erscheinen mit `€`. Die Zahlen
bleiben unveränderte virtuelle Kontoeinheiten; dies ist keine Umrechnung in
Echtgeld. Bestehende `anoap`-Befehle und Datenbanknamen bleiben kompatibel.

## Biggest Boy: verbindlicher 5×3-Expanding-Slot

Neue Runden verwenden `anocore-biggest-boy-5x3-expanding-v4`: 15 sichtbare Felder,
dieselben zehn Linien wie die Freispiele-Slot, Biggest-Boy-Schärpe als Wild, Döner als
reguläres Top-Symbol und Gesicht als Scatter. 3/4/5 Scatter starten 10/15/20
Freispiele; erneute Trigger geben fünf weitere, maximal 30 tatsächlich gespielte.
Ein gespeichertes reguläres Expanding Symbol füllt bei einem Treffer genau drei
Positionen seiner Walze. Auswertung auf dem expandierten Feld; Retrigger anhand
des unveränderten Rohfelds. Keine Collector-Multiplikatoren oder nachträgliche
Gewinnkürzung in v4. Bestehende v2-/v3-Runden behalten ihre ursprünglichen Regeln.

Vollständiger Gewinnplan, Mathematik, Simulation, UI-/Bewegungsmodell, Legacy-
Kompatibilität und native Abnahmeschritte: [Biggest Boy](biggest-boy.md).
Alle zwölf Originalsymbole sind enthalten. Der dedizierte v4-Build prüft die
Bildvollständigkeit vor dem Kompilieren. Native Valve-/CS2-Abnahme bleibt erforderlich.

### Biggest Boy: wählbares Gewinnprofil

`BiggestBoyProfile` in `config/core.json` akzeptiert `standard`, `frequent` oder
`generous`. Nach Änderung Plugin neu laden; gespeicherte Boni behalten ihr Profil.
RTP, Stoppgewichte, Trefferhäufigkeiten und die aktuelle Wild-/Scatter-Bildzuordnung
stehen in [biggest-boy.md](biggest-boy.md). Süßilix löst Freispiele aus, die
Biggest-Boy-Schärpe ersetzt reguläre Symbole als Wild. Keine Echtgeld-Abrechnung.


## Maximale Starteinsätze pro Spiel

Ab dem Coinflip-/Limit-Update kann `config/core.json` folgenden Block enthalten.
Bestehende Verbindung und übrige Einstellungen behalten; keine produktiven
Zugangsdaten in Issues, Pull Requests oder Beispieldateien ablegen.

```json
{
  "CasinoMaximumStakes": {
    "Slots": 10000,
    "Coinflip": 10000,
    "Roulette": 10000,
    "Blackjack": 10000,
    "Risk": 10000,
    "Book": 10000
  },
  "BiggestBoyMaximumStake": 10000,
  "BiggestBoyProfile": "generous"
}
```

Die sechs neuen Felder akzeptieren 50 bis 1.000.000 in Schritten von 50; fehlende
Felder bleiben bei 500. Biggest Boy behält seine vorhandene Grenze bis 10.000 und
seine festen Einsatzstufen. Nach Änderung Plugin kontrolliert neu laden oder
Testserver neu starten. Ein `null`-Block und ungültige Werte werden abgewiesen.
Die Einsatzsteuerungen verwenden die jeweilige Grenze und reduzieren beim
Spielwechsel einen zu hohen ausgewählten Einsatz. Die serverseitige Buchung
prüft dieselben Grenzen vor der Ziehung. Pure Gewinnregeln akzeptieren höhere
Einsätze, behalten aber ihre bisherigen Gewinnfaktoren und Wahrscheinlichkeiten.

Es handelt sich um den **anfänglichen Einsatz**, nicht um einen Höchstgewinn oder
eine Gesamtkostenbegrenzung beim Blackjack: Double/Split können zusätzliche
Einsätze einer bereits begonnenen Runde buchen. Vorhandene Kontolimits und
Maximalgewinn-Reservierungen bleiben wirksam; sie können trotz erlaubtem Einsatz
einen Start ablehnen. Risk behält seine absolute Grenze für den gehaltenen Betrag
und maximal fünf Verdopplungen. Kontoaufladungen bleiben eine separate Funktion.

Bereits gebuchte Aktionen werden auch nach einer Verringerung des Limits
unverändert wiedergegeben; laufende Runden und Freispiele behalten ihren Einsatz.
Nur neue Starts unterliegen dem neuen Limit. Keine Migration aktiver Spielstände
und keine nachträgliche Neuberechnung von Auszahlungen.

## Coinflip mit Originalbildern

`kopf.png` wird als `coin_heads.png`, `zahl.png` als `coin_tails.png` bytegleich
übernommen. Zuordnung und SHA-256 stehen in `coinflip-original-assets.json`.
Beide PNG/VTEX-Paare gehören zum vollständigen und zum Casino-Addon-Build.
Die Dateizuordnung bestimmt die Seite; die Abbildungen werden nicht umgestaltet.

Während des Wurfs läuft eine native 480-ms-Schleife: zwei Bildflächen wechseln
mit komplementärer Deckkraft und kurzen sichtbaren Haltephasen an den schmalen
Kantenphasen. Aufeinanderfolgende Würfe wechseln die Animationsklasse. Das
gebuchte Ergebnis wird währenddessen noch nicht als Ergebnistext gezeigt.
Bei 3,6 Sekunden beendet der bestehende Presenter die Schleife und setzt die
gebuchte Seite samt Kopf-/Zahl-Text; der Gewinn wird wie bisher danach angezeigt.
Animation und Timer führen keine neue Zufallsziehung oder AP-Buchung aus.

Automatisiert geprüft werden beide Landeseiten, Wiederholungswürfe, unveränderte
Buchungsanzahl, Wechselphasen, Originaldateien, beide Buildpfade, voneinander
unabhängige Einsatzgrenzen, größere Auszahlungen und Restore/Replay bei reduzierten
Grenzen. Native Valve-Kompilierung und Sichtprüfung im CS2-Client bleiben vor
Workshop-Veröffentlichung erforderlich.

## Glücksregler, native Walzen-Klicks und Kokosnuss für Book (#506)

In `addons/counterstrikesharp/plugins/AnoCore/config/core.json` kann ein
Serverbetreiber **einen numerischen Glückswert zwischen 0 und 100**
konfigurieren (zusätzliche vorhandene Einstellungen beibehalten):

```json
{
  "BiggestBoyProfile": "standard",
  "BiggestBoyLuckPercent": 85,
  "CasinoReelStopSoundEnabled": true
}
```

- **0** verwendet das vorhandene Profil (standard/frequent/generous)
  **unverändert**, ohne zusätzliche RNG-Aufrufe.
- **1–99**: mit genau dieser Wahrscheinlichkeit wird bei jedem Spin eine
  bestimmte vorhandene und mathematisch zulässige 5-Walzen-Position gezogen,
  auf der mindestens ein **5er-Liniengewinn** und **drei Scatter** auftreten.
  Der nicht erzwungene Anteil verwendet die normalen Walzenstopps, sodass
  die tatsächliche Gesamtchance auch höher als der Zahlenwert sein kann.
- **100** garantiert für **bezahlte Spins** einen Liniengewinn sowie einen
  Freispieltrigger. Dies gilt nur für neue Biggest-Boy-Runden.
  Freispiele übernehmen das Profil unverändert; ihre **Rohfelder** besitzen
  ebenfalls garantiert Scatter, doch ein Expanding Symbol kann während der
  Freispielberechnung einen Teil der Gewinnlinien überdecken.
- Es gibt keine clientseitig erfundenen Gewinnsymbole oder Auszahlungen.
  Sonderstopps verwenden `[50, 0, 49]` auf den ersten drei Walzen und
  zufällig eine der jeweils 14 O-Positionen auf Walze vier und fünf.
  Auf der dritten Linie erscheinen dadurch stets 5× O, auf den
  ersten drei Walzen insgesamt 3× Scatter. Die letzten beiden Walzen
  sind nicht auf dieselbe Bildfolge festgelegt. Gewinnfaktoren und
  Guthaben-Reservierungen bleiben **unverändert**.
- Der Wert wird beim Start als `lucky:N` im persistierten Rundenstatus
  gespeichert, damit Änderungen am Server keine begonnenen Boni umstellen.
  Er wirkt **global** für Biggest Boy, nicht nur für ausgewählte Spieler,
  und erhöht nicht die Book-Gewinnwahrscheinlichkeit.

Die laufende Animation gibt beim Stopp jeder der fünf Walzen genau einen
**lokalen** CS2-Standardsound `sounds/ui/counter_beep.vsnd` an den
betroffenen Spieler aus. Keine neue Sounddatei und kein neues Workshop-Audio
nötig. Per `CasinoReelStopSoundEnabled: false` ausschaltbar. Klang,
Clientberechtigung und Lautstärke sind auf dem CS2-Testserver zu prüfen.

Das abgeschnittene zweite Textfeld in den oberen zwei Konto-Boxen wird durch
84px hohe Kontokästen, 100px Gesamtbereich, kleinere Titelabstände
und entfernte vererbte Label-Ränder korrigiert.

In der neuen Book-/Früchte-Oberfläche ersetzt die
**vom Nutzer hochgeladene goldene Kokosnuss** das bisher textuelle
`BUCH` bei Gewinnreihen **und** in bewegten Bildfeldern. Das Wild-/Scatter-
Verhalten bleibt das des ursprünglichen Buchs. Die Quelldatei aus der
Chatdatei heißt `image(20261015-151531).png`. Ihre Prüfsumme lautet
`SHA256 1e8f1cbcb2c6c174a68cb5c50acbebbf39a1e21f9397e91d44ae046388805604`.
Sie muss in der Verzeichnisstruktur unter
`ui/AnoCore/styles/custom_game/anocore/casino/book_coconut.png`
liegen. Dazu gibt es ein separates ZIP mit der unveränderten Original-PNG.

Windows: GitHub-Quellstand und Originalbild-ZIP in **denselben Hauptordner**
entpacken, PowerShell dort öffnen, dann:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\ui\AnoCore\build-biggest-boy.ps1 -Addon anomeme_ui -InstallLocalClient
```

Das Build-Skript verweigert nun die native Kompilierung, wenn die
Kokosnuss-Datei fehlt. Alternativ `-BookCoconutPng
"<original-uploaded-image.png>"` übergeben. Ein reines GitHub-CI-Paket
enthält die vom Nutzer über den Chat gelieferte PNG noch nicht; **zuerst**
das separate Originalbild-ZIP einfügen. Beim ersten Test native
`.vtex_c`-Ausgabe, die alte Workshop-Assetcache-Version und
fünf stoppende Walzen einschließlich Sound prüfen. Kein Merge ohne Abnahme.
