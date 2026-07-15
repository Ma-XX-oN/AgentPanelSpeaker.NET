@echo off
setlocal
cd /d "%~dp0"

dotnet restore AgentPanelSpeaker\AgentPanelSpeaker.csproj --configfile NuGet.Config
if errorlevel 1 exit /b %errorlevel%

dotnet publish AgentPanelSpeaker\AgentPanelSpeaker.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  --no-restore ^
  -o publish\win-x64
exit /b %errorlevel%
