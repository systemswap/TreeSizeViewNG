@echo off
setlocal EnableExtensions
rem Keine Nutzungsdaten an Hersteller senden (.NET, Avalonia, ONNX Runtime, Testlaeufer).
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "AVALONIA_TELEMETRY_OPTOUT=1"
set "ORT_DISABLE_TELEMETRY=1"
set "TESTINGPLATFORM_TELEMETRY_OPTOUT=1"
set "DO_NOT_TRACK=1"
cd /d "%~dp0"

set "PROJECT=TreeSizeViewNG.csproj"
set "OUTPUT=publish\windows-x64"

echo ==========================================
echo TreeSizeViewNG - Windows x64 veroeffentlichen
echo ==========================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo FEHLER: Das .NET SDK wurde nicht gefunden.
    echo Installiere das .NET 10 SDK und starte das Skript erneut.
    pause
    exit /b 1
)

for /f "tokens=1 delims=." %%A in ('dotnet --version') do set "SDK_MAJOR=%%A"
if not defined SDK_MAJOR (
    echo FEHLER: Die installierte .NET-SDK-Version konnte nicht ermittelt werden.
    pause
    exit /b 1
)
if %SDK_MAJOR% LSS 10 (
    echo FEHLER: Mindestens das .NET 10 SDK wird benoetigt.
    echo Gefunden:
    dotnet --version
    pause
    exit /b 1
)

if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"

rem Schneller Start: ReadyToRun (vorkompiliert) als eine einzige exe, ohne Kompression.
rem Gemessen startete das schneller als Native AOT (Avalonia/Skia braucht unter AOT
rem laenger zum Initialisieren) und als die frueher komprimierte Single-File-exe,
rem die bei jedem Start erst entpackt werden musste. Preis: die exe ist groesser (~136 MB).
dotnet publish "%PROJECT%" ^
  --nologo ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained true ^
  --output "%OUTPUT%" ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=false ^
  -p:PublishReadyToRun=true ^
  -p:PublishTrimmed=false ^
  -p:DebugType=None ^
  -p:DebugSymbols=false

if errorlevel 1 (
    echo.
    echo FEHLER: Die Veroeffentlichung ist fehlgeschlagen.
    pause
    exit /b 1
)

rem Zusaetzlich als ZIP mit der Versionsnummer im Namen (aeltere ZIPs werden ersetzt)
del /q "publish\TreeSizeViewNG-*-windows-x64.zip" >nul 2>nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "$v = (Get-Item '%OUTPUT%\TreeSizeViewNG.exe').VersionInfo.ProductVersion.Split('+')[0]; $zip = 'publish\TreeSizeViewNG-' + $v + '-windows-x64.zip'; Compress-Archive -Force -Path '%OUTPUT%\TreeSizeViewNG.exe' -DestinationPath $zip; Write-Host ('Version ' + $v + ': ' + $zip)"

echo.
echo Fertig: %~dp0%OUTPUT%\TreeSizeViewNG.exe
pause
