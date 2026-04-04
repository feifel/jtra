# Runtime Error Due to Inconsistent SDK Versions

## Error

```
Error: AggregateException_ctor_DefaultMessage (Method not found:
  Microsoft.Extensions.Logging.ILoggingBuilder
  Microsoft.AspNetCore.Components.WebAssembly.Hosting.WebAssemblyHostBuilder.get_Logging())
```

This is a `MissingMethodException` thrown in the browser when the Blazor WASM app starts.

## Root Cause

The `_framework/` directory served by the ASP.NET Core static files middleware contained files from **mixed SDK builds**:

- `JtraClient.wasm` — compiled with SDK `8.0.125` (ships runtime `8.0.12`)
- `Microsoft.AspNetCore.Components.WebAssembly.wasm` — from NuGet package `8.0.25`

When the browser loaded these together, the compiled app called `WebAssemblyHostBuilder.get_Logging()` — a method that exists in `8.0.25` but whose ABI differs from what the `8.0.12`-era compiler expected. This caused a `MissingMethodException` at startup.

### Why the mismatch occurred

1. **`global.json` pinned SDK to `8.0.125`** (the `1xx` series, which ships runtime `8.0.12`), while NuGet packages in `JtraClient.csproj` were pinned to `8.0.25`.
2. **`src/Dockerfile` used floating tags** (`sdk:8.0`, `aspnet:8.0`) — Docker pulled whatever the latest image was at build time, which could differ from the local SDK.
3. **`global.json` was outside the Docker build context** (`src/`) — Docker never saw it, so the SDK version inside Docker was uncontrolled.
4. **`src/JtraServer/wwwroot/_framework/`** (excluded from `.gitignore`) could contain stale files from a previous build with a different SDK, which the dev server would serve without rebuilding.

## SDK Version Series Explained

.NET 8 has two SDK series per runtime patch:

| SDK series | Example | Ships runtime |
|---|---|---|
| `1xx` | `8.0.125` | `8.0.12` |
| `4xx` | `8.0.419` | `8.0.25` |

Mixing a `1xx` SDK with `8.0.25` NuGet packages is the direct cause of the error.

## Fixes Applied

### 1. `global.json` — Updated SDK to `8.0.419`

```json
{
  "sdk": {
    "version": "8.0.419",
    "rollForward": "disable",
    "allowPrerelease": false
  }
}
```

SDK `8.0.419` ships runtime `8.0.25`, which matches the NuGet packages in `JtraClient.csproj`.

### 2. `~/.bashrc` — Added `~/.dotnet` to PATH

SDK `8.0.419` was installed to `~/.dotnet` via the official install script. The system `dotnet` at `/usr/bin/dotnet` is still `8.0.125`. The following was appended to `~/.bashrc` to ensure the correct SDK is used:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
```

Open a new terminal (or run `source ~/.bashrc`) for this to take effect. Verify with `dotnet --version` → should show `8.0.419`.

### 3. `src/Dockerfile` — Pinned image tags

Replaced floating `:8.0` tags with explicit version tags:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0.25 AS base
FROM mcr.microsoft.com/dotnet/sdk:8.0.419 AS build
FROM mcr.microsoft.com/dotnet/sdk:8.0.419 AS client-build
```

This ensures Docker builds always use the same SDK regardless of when the image is pulled.

### 4. `src/publish.sh` — Clean `_framework` before publishing

Added a cleanup step before publishing to prevent stale files from a previous build being served:

```bash
echo "Cleaning stale framework files..."
rm -rf "$SERVER_DIR/wwwroot/_framework"
```

### 5. `wasm-tools` workload — Installed for SDK `8.0.419`

```bash
dotnet workload install wasm-tools
```

This eliminates the build warning and ensures the WASM toolchain matches the SDK.

## Verification

After applying all fixes, a clean publish produces consistent output:

- `JtraClient.wasm` — compiled by SDK `8.0.419` with `WasmToolsPackageVersion` `8.0.25`
- All framework DLLs — from NuGet `8.0.25`
- No version mismatch → no `MissingMethodException` at runtime

## Consistent Version Matrix

| Component | Version |
|---|---|
| .NET SDK | `8.0.419` |
| Runtime | `8.0.25` |
| `Microsoft.AspNetCore.Components.WebAssembly` | `8.0.25` |
| `WasmToolsPackageVersion` | `8.0.25` |
| Docker SDK image | `mcr.microsoft.com/dotnet/sdk:8.0.419` |
| Docker runtime image | `mcr.microsoft.com/dotnet/aspnet:8.0.25` |
