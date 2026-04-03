#!/usr/bin/env pwsh
# Build and run JTRA on Windows PowerShell.
# This script publishes the Blazor WASM client and server, copies client files into
# the server publish wwwroot, then runs the published server.

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$clientDir = Join-Path $scriptDir "JtraClient"
$serverDir = Join-Path $scriptDir "JtraServer"
$clientPublishDir = Join-Path $clientDir "bin/BlazorPublish"
$serverPublishDir = Join-Path $serverDir "bin/ServerPublish"

Write-Host "Publishing Blazor client (Release)..."
& dotnet publish (Join-Path $clientDir "JtraClient.csproj") -c Release -o $clientPublishDir --nologo -v quiet

Write-Host "Publishing server (Release)..."
& dotnet publish (Join-Path $serverDir "JtraServer.csproj") -c Release -o $serverPublishDir --nologo -v quiet

Write-Host "Copying client files to server wwwroot..."
$serverWwwrootDir = Join-Path $serverPublishDir "wwwroot"
New-Item -ItemType Directory -Path $serverWwwrootDir -Force | Out-Null
Copy-Item -Path (Join-Path $clientPublishDir "wwwroot/*") -Destination $serverWwwrootDir -Recurse -Force

Write-Host "Starting published server..."
Push-Location $serverPublishDir
try {
	& dotnet "./JtraServer.dll" @args
}
finally {
	Pop-Location
}
