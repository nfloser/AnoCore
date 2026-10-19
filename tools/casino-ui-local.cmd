@echo off
setlocal
cd /d "%~dp0.."
echo CS2 vollstaendig schliessen. CS2 Workshop Tools muessen installiert sein.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\ui\AnoCore\build.ps1" -Addon anomeme_ui -RequireSpitzeSkins -InstallLocalClient
if errorlevel 1 (
  echo FEHLER: UI wurde nicht vollstaendig gebaut. Bitte die Fehlermeldung oben pruefen.
  pause
  exit /b 1
)
echo UI fertig. Jetzt CS2 neu starten und mit dem Testserver verbinden.
echo Fuer andere Spieler muss das bestehende anomeme_ui Workshop-Addon aktualisiert werden.
pause
