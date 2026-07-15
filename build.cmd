@echo off
setlocal
cd /d "%~dp0"

dotnet restore AgentPanelSpeaker\AgentPanelSpeaker.csproj --configfile NuGet.Config
if errorlevel 1 exit /b %errorlevel%

dotnet build AgentPanelSpeaker\AgentPanelSpeaker.csproj -c Release --no-restore
exit /b %errorlevel%
