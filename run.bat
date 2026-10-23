@echo off
set "DOTNET_EXE=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
if exist "%DOTNET_EXE%" (
    "%DOTNET_EXE%" run -- %*
) else (
    dotnet run -- %*
)
