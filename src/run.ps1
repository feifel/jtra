#!/usr/bin/env pwsh
# Run previously published JTRA server on Windows PowerShell.

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverPublishDir = Join-Path (Join-Path $scriptDir "JtraServer") "bin/ServerPublish"
$serverDllPath = Join-Path $serverPublishDir "JtraServer.dll"

if (-not (Test-Path $serverDllPath)) {
	throw "Published server not found at '$serverDllPath'. Run ./publish.ps1 first."
}

Write-Host "Starting published server..."
Push-Location $serverPublishDir
try {
	& dotnet "./JtraServer.dll" @args
}
finally {
	Pop-Location
}
