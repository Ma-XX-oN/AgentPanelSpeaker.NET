@echo off
setlocal
cd /d "%~dp0"
dotnet run -c Release
