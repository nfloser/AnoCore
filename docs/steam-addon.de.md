# Steam-Workshop-Addon: Overlay und Logo

Diese Anleitung beschreibt den vorhandenen Windows-Build für `anomeme_ui` und Updates
am bestehenden ANOMEME-Workshop-Eintrag. Der Quellcode und das Logo liegen im Repository.

## Dateien im Repository

| Inhalt | Pfad |
| --- | --- |
| Logo-Banner | `ui/AnoCore/styles/custom_game/anocore/anomeme_banner.png` |
| Texturdescriptor | `ui/AnoCore/styles/custom_game/anocore/anomeme_banner.vtex` |
| Menüaufbau | `ui/AnoCore/layout/custom_game/anocore/menu.xml` |
| Menüdesign | `ui/AnoCore/styles/custom_game/anocore/menu.css` |
| Veto-Aufbau | `ui/AnoCore/layout/custom_game/anocore/ano_veto.xml` |
| Veto-Design | `ui/AnoCore/styles/custom_game/anocore/ano_veto.css` |
| Build-Skript | `ui/AnoCore/build.ps1` |

Panel- und Button-IDs beibehalten: Das Serverplugin verwendet sie.
Das PNG nicht nur in einen Downloadordner legen: Der Build kopiert Bild und Descriptor ins Addon
und erzeugt die vom Menü verwendete `.vtex_c`-Ressource.

## 1. Ordner öffnen

Repository herunterladen/klonen oder das CI-Paket entpacken. In VS Code den Ordner öffnen,
in dem direkt `ui` liegt. Alternativ `AnoCore-source.zip` entpacken und dessen Wurzel öffnen.
Terminal → Neues Terminal → PowerShell. Prüfen:

```powershell
Test-Path .\ui\AnoCore\build.ps1
```

Erwartet: `True`. Änderungen zuerst mit Strg+S speichern.

## 2. Addon bauen

```powershell
powershell -ExecutionPolicy Bypass -File .\ui\AnoCore\build.ps1 -Addon anomeme_ui
```

Das Skript sucht CS2 in den Steam-Bibliotheken und kompiliert Logo, Menü und Veto mit erzwungener Aktualisierung.
Bei abweichender Installation den tatsächlichen Pfad verwenden, beispielsweise:

```powershell
powershell -ExecutionPolicy Bypass -File .\ui\AnoCore\build.ps1 -Addon anomeme_ui -Cs2 "D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"
```

Erst nach `AnoCore Panorama HUD compiled successfully.` und ohne Compile-Fehler hochladen.
Fehlt `resourcecompiler.exe`, CS2 Workshop Tools installieren. Bei `False` in Schritt 1 den richtigen Ordner öffnen.
Kein manuelles Löschen alter Ressourcen nötig. `-InstallLocalClient` für normale Workshop-Tests weglassen;
lokal installierte Ressourcen können das tatsächliche Workshop-Ergebnis verdecken.

## 3. Wo liegt das Addon? (Win+R)

Bei Standardinstallation diese Pfade in Win+R einfügen; bei anderer Steam-Bibliothek den Anfang anpassen.

**Quelldateien:**

```text
C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\content\csgo_addons\anomeme_ui\panorama
```

**Kompiliertes Addon:**

```text
C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo_addons\anomeme_ui
```

Darunter liegt das kompilierte Logo bei `panorama\styles\custom_game\anocore\anomeme_banner.vtex_c`.
Quellen und kompilierte Ressourcen sind getrennte Ordner; das Build-Skript verwaltet beide.

## 4. Workshop-Inhalt hochladen

CS2 Workshop Tools starten, `anomeme_ui` auswählen, Workshop Manager öffnen.
Bestehenden Eintrag **AnoCore UI** wählen → **Re-Upload → anomeme_ui → Submit**.
Den Inhalt aktualisieren; nur die Beschreibung zu ändern reicht nicht.
Upload und gegebenenfalls Freigabe abwarten. Den bestehenden Eintrag nicht löschen oder neu anlegen.

Die ANOMEME-ID ist **3815363712**. Sie steht hinter `id=` in der Workshop-Adresse:
https://steamcommunity.com/sharedfiles/filedetails/?id=3815363712

Für einen eigenen Server mit eigenem Workshop-Eintrag dessen eigene ID verwenden.
Der Ordnername `anomeme_ui` ist keine Workshop-ID.

## 5. Heruntergeladene Workshop-Kopie finden

Win+R:

```text
C:\Program Files (x86)\Steam\steamapps\workshop\content\730\3815363712
```

`730` ist die CS2-App-ID, `3815363712` die Addon-ID. Der Ordner existiert erst,
wenn Steam den Inhalt dort heruntergeladen hat; gegebenenfalls andere Steam-Bibliothek prüfen.
Diese Downloadkopie nicht für den Upload bearbeiten.

## 6. Server verbinden und testen

Das vorhandene MultiAddonManager-Setup verwendet:

```text
mm_extra_addons "3815363712"
mm_addon_mount_download "1"
```

Weitere Addon-IDs behalten; nicht blind die komplette Liste ersetzen. Die Einstellungen bleiben
in der vorhandenen Server-/MultiAddonManager-CFG und gehören nicht in `core.json`.
Server und CS2 neu starten, verbinden, im Spielchat `!anomenu` eingeben.
Das Dashboard braucht den passenden Serverbuild, nicht nur die neuen XML/CSS-Dateien.
Siehe [Schnellinstallation](quick-install.de.md).

## Änderungen und Fehlersuche

- Logo ändern: PNG unter dem oben angegebenen Repo-Pfad ersetzen, Descriptor behalten, bauen, Workshop-Inhalt aktualisieren.
- Farben/Abstände ändern: `menu.css` bearbeiten, speichern, bauen und hochladen.
- Layout ändern: `menu.xml` bearbeiten; bei neuen Panel-IDs auch das Serverplugin anpassen.
- Daten fehlen: Pluginversion, aktivierte Module, Datenbankstart und `!anostatus` prüfen.
- Alte Grafik: Build-Erfolg, tatsächlich hochgeladenen Addon-Inhalt und Download/Serverneustart prüfen.
- Back und Seitenwechsel getrennt prüfen: Back wechselt die Ansicht, Prev/Next page blättern.

CI prüft Quellen und Servercode; Valve-Kompilierung, Mausbedienung und Bildschirmabdeckung
müssen in CS2 getestet werden. Bestehende Dateien vor dem ersten Update sichern.
