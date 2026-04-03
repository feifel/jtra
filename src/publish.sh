#!/bin/bash
# Publish JTRA.
# This script publishes the Blazor WASM client and server, then copies client files
# into the server publish wwwroot.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLIENT_DIR="$SCRIPT_DIR/JtraClient"
SERVER_DIR="$SCRIPT_DIR/JtraServer"
CLIENT_PUBLISH_DIR="$CLIENT_DIR/bin/BlazorPublish"
SERVER_PUBLISH_DIR="$SERVER_DIR/bin/ServerPublish"

echo "Publishing Blazor client (Release)..."
dotnet publish "$CLIENT_DIR/JtraClient.csproj" -c Release -o "$CLIENT_PUBLISH_DIR" --nologo -v quiet

echo "Publishing server (Release)..."
dotnet publish "$SERVER_DIR/JtraServer.csproj" -c Release -o "$SERVER_PUBLISH_DIR" --nologo -v quiet

echo "Copying client files to server wwwroot..."
mkdir -p "$SERVER_PUBLISH_DIR/wwwroot"
cp -r "$CLIENT_PUBLISH_DIR/wwwroot/." "$SERVER_PUBLISH_DIR/wwwroot/"

echo "Publish completed."
