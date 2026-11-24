#!/usr/bin/env bash
set -euo pipefail
# Keine Nutzungsdaten an Hersteller senden (.NET, Avalonia, ONNX Runtime, Testläufer),
# auch wenn es im System nicht abgeschaltet ist.
export DOTNET_CLI_TELEMETRY_OPTOUT=1 AVALONIA_TELEMETRY_OPTOUT=1 ORT_DISABLE_TELEMETRY=1 TESTINGPLATFORM_TELEMETRY_OPTOUT=1 DO_NOT_TRACK=1

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

PROJECT="TreeSizeViewNG.csproj"

echo "=========================================="
echo "TreeSizeViewNG - Veröffentlichung"
echo "=========================================="
echo
echo "1) Linux x64"
echo "2) Windows x64"
echo "3) Linux und Windows"
echo
read -r -p "Auswahl [1-3]: " choice

if ! command -v dotnet >/dev/null 2>&1; then
    echo "FEHLER: Das .NET SDK wurde nicht gefunden."
    echo "Installiere das .NET 10 SDK und starte das Skript erneut."
    exit 1
fi

sdk_version="$(dotnet --version)"
sdk_major="${sdk_version%%.*}"
if [[ ! "$sdk_major" =~ ^[0-9]+$ ]] || (( sdk_major < 10 )); then
    echo "FEHLER: Mindestens das .NET 10 SDK wird benötigt."
    echo "Gefunden: $sdk_version"
    exit 1
fi

publish_target() {
    local rid="$1"
    local output="$2"

    echo
    echo "Veröffentliche für $rid ..."
    rm -rf "$output"

    dotnet publish "$PROJECT" \
      --nologo \
      --configuration Release \
      --runtime "$rid" \
      --self-contained true \
      --output "$output" \
      -p:PublishSingleFile=true \
      -p:IncludeNativeLibrariesForSelfExtract=true \
      -p:IncludeAllContentForSelfExtract=true \
      -p:EnableCompressionInSingleFile=true \
      -p:PublishTrimmed=false \
      -p:PublishReadyToRun=false \
      -p:DebugType=None \
      -p:DebugSymbols=false
}

case "$choice" in
    1)
        publish_target "linux-x64" "publish/linux-x64"
        ;;
    2)
        publish_target "win-x64" "publish/windows-x64"
        ;;
    3)
        publish_target "linux-x64" "publish/linux-x64"
        publish_target "win-x64" "publish/windows-x64"
        ;;
    *)
        echo "Ungültige Auswahl."
        exit 1
        ;;
esac

echo
echo "Fertig. Ausgaben befinden sich unter:"
echo "  $SCRIPT_DIR/publish"
