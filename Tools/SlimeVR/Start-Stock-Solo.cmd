@echo off
setlocal
set "JAVA_HOME=%~dp0jre"
set "PATH=%~dp0;%PATH%"
rem The pinned GUI consumes its first Windows argument as the Node script name.
start "" "%~dp0SlimeVR.exe" --no-first-run --path "%~dp0Stock"
