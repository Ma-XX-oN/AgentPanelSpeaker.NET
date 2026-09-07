@echo off
setlocal
cd /d "%~dp0"

dotnet run --project AgentPanelSpeaker\AgentPanelSpeaker.csproj -c Release --no-build -- %*
exit /b %errorlevel%
