#!/bin/bash
# Run previously published JTRA server.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVER_PUBLISH_DIR="$SCRIPT_DIR/JtraServer/bin/ServerPublish"
SERVER_DLL="$SERVER_PUBLISH_DIR/JtraServer.dll"

if [ ! -f "$SERVER_DLL" ]; then
	echo "Published server not found at '$SERVER_DLL'. Run ./publish.sh first." >&2
	exit 1
fi

echo "Starting published server..."
cd "$SERVER_PUBLISH_DIR"
dotnet "./JtraServer.dll" "$@"
