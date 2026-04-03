#!/usr/bin/env pwsh
# Run JTRA in development mode with hot reload on Windows PowerShell.
# The server's MSBuild target automatically publishes the client on build.

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverDir = Join-Path $scriptDir "JtraServer"

# Clean stale framework files to avoid SRI hash mismatches
$frameworkDir = Join-Path $serverDir "wwwroot/_framework"
if (Test-Path $frameworkDir) {
	Write-Host "Cleaning stale framework files..."
	Remove-Item -Recurse -Force $frameworkDir
}

Write-Host "Starting JTRA in development mode with hot reload..."
Write-Host ""

Push-Location $serverDir
try {
	& dotnet watch run @args
}
finally {
	Pop-Location
}
