#!/bin/bash
# Build and run JTRA
# This script publishes the Blazor WASM client, copies it into the server's wwwroot, then runs the server.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLIENT_DIR="$SCRIPT_DIR/JtraClient"
SERVER_DIR="$SCRIPT_DIR/JtraServer"
PUBLISH_DIR="$CLIENT_DIR/bin/BlazorPublish"

echo "Publishing Blazor client..."
dotnet publish "$CLIENT_DIR/JtraClient.csproj" -c Debug -o "$PUBLISH_DIR" --nologo -v quiet

echo "Copying client files to server wwwroot..."
cp -r "$PUBLISH_DIR/wwwroot/." "$SERVER_DIR/wwwroot/"

echo "Starting server..."
dotnet run --project "$SERVER_DIR/JtraServer.csproj" --no-build "$@"
