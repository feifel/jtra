#!/bin/bash
# Run JTRA in development mode with hot reload.
# The server's MSBuild target automatically publishes the client on build.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVER_DIR="$SCRIPT_DIR/JtraServer"

# Clean stale framework files to avoid SRI hash mismatches
FRAMEWORK_DIR="$SERVER_DIR/wwwroot/_framework"
if [ -d "$FRAMEWORK_DIR" ]; then
	echo "Cleaning stale framework files..."
	rm -rf "$FRAMEWORK_DIR"
fi

echo "Starting JTRA in development mode with hot reload..."
echo ""

cd "$SERVER_DIR"
dotnet watch run "$@"

