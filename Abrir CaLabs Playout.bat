@echo off
rem Abre CaLabs Playout como aplicacion (usa Chrome; si no esta, usa Edge)
set "F=%~dp0CaLabs_Playout.html"
set "F=%F:\=/%"
set "PERFIL=%LOCALAPPDATA%\CaLabsPlayout"
set "OPC=--app="file:///%F%" --user-data-dir="%PERFIL%" --autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --start-maximized"
set "NAV=msedge"
reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe" >nul 2>&1 && set "NAV=chrome"
reg query "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe" >nul 2>&1 && set "NAV=chrome"
start "" %NAV% %OPC%
