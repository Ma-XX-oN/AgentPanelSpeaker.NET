@echo off
setlocal
pushd "%~dp0"
dotnet run --project SpeechProgressHarness.csproj -c Release -- %*
set EXIT_CODE=%ERRORLEVEL%
popd
exit /b %EXIT_CODE%
