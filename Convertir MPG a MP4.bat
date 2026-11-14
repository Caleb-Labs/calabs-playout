@echo off
setlocal
chcp 65001 >nul
title CaLabs - Convertir a MP4
rem ---------------------------------------------------------------
rem  Convierte MPG, AVI, WMV, TS, VOB... a MP4 (H.264) para CaLabs Playout.
rem  - Doble clic: convierte los videos de ESTA carpeta.
rem  - O arrastra carpetas o archivos encima de este archivo.
rem  Los MP4 quedan en una carpeta "MP4" junto a los originales.
rem  Los originales NO se borran. Necesita ffmpeg.exe junto a este archivo.
rem ---------------------------------------------------------------
set "FF=ffmpeg"
if exist "%~dp0ffmpeg.exe" set "FF=%~dp0ffmpeg.exe"
"%FF%" -version >nul 2>&1
if errorlevel 1 goto sinffmpeg
if "%~1"=="" goto estacarpeta
:siguiente
if "%~1"=="" goto fin
if exist "%~1\" goto escarpeta
call :convertir "%~1"
goto avanzar
:escarpeta
pushd "%~1"
call :carpeta
popd
:avanzar
shift
goto siguiente
:estacarpeta
pushd "%~dp0"
call :carpeta
popd
goto fin
:carpeta
for %%E in (mpg mpeg avi wmv ts vob mts m2ts m2v flv) do for %%F in (*.%%E) do call :convertir "%%~fF"
exit /b
:convertir
set "SRC=%~1"
set "NOM=%~n1"
set "DIR=%~dp1"
set "TXT=%~nx1"
if "%TXT:~0,2%"=="._" exit /b
if exist "%DIR%MP4\%NOM%.mp4" goto yaexiste
if not exist "%DIR%MP4" mkdir "%DIR%MP4"
echo.
echo Convirtiendo: %TXT%
"%FF%" -hide_banner -loglevel error -stats -y -i "%SRC%" -vf "yadif=deint=interlaced" -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart "%DIR%MP4\%NOM%.mp4"
exit /b
:yaexiste
echo Ya existe, se salta: %TXT%
exit /b
:fin
echo.
echo Listo. Los MP4 estan en la carpeta MP4 junto a cada video.
pause
exit /b
:sinffmpeg
echo.
echo No encuentro FFmpeg en este PC.
echo  1. Descargalo desde ffmpeg.org (seccion Download, Windows).
echo  2. Copia el archivo ffmpeg.exe en la MISMA carpeta que este archivo.
echo  3. Abre este archivo otra vez.
pause
exit /b 1
