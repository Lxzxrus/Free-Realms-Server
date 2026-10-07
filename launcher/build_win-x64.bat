@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Version number is required.
    echo Usage: build.bat [version] [extra_args...]
    exit /b 1
)

set "version=%~1"

echo.
echo Compiling Launcher with dotnet...
echo %~dp0publish
dotnet publish .\src\Launcher\Launcher.csproj -c Release --no-self-contained -r win-x64 --property:PublishDir="%~dp0publish"

rem Name, authors and app id come from src\Directory.Build.props.
for /f "delims=" %%i in ('dotnet msbuild .\src\Launcher\Launcher.csproj -getProperty:LauncherTitle') do set "title=%%i"
for /f "delims=" %%i in ('dotnet msbuild .\src\Launcher\Launcher.csproj -getProperty:LauncherAuthors') do set "authors=%%i"
for /f "delims=" %%i in ('dotnet msbuild .\src\Launcher\Launcher.csproj -getProperty:LauncherId') do set "appid=%%i"

echo.
echo Building Velopack Release v%version%
vpk pack --packTitle "%title%" --packAuthors "%authors%" -u "%appid%" -e Launcher.exe -o "%~dp0releases" -p "%~dp0publish" -i "%~dp0publish\App.ico" -f net10-x64-desktop -v %*