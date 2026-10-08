@echo off
setlocal
set "APP=%~dp0dist\Ntag5Studio-1.4.1\Ntag5Studio.exe"
if exist "%APP%" goto run
echo Ntag5 Studio has not been published. Run the build/publish step first.
pause
exit /b 1

:run
start "" "%APP%"
endlocal
