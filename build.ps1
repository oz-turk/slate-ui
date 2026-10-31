$root = $PSScriptRoot

# 1. Svelte build → src/Resources/
Write-Host "Building Svelte UI..."
Push-Location "$root\ui"
npm run build
if ($LASTEXITCODE -ne 0) { Write-Host "Svelte build failed."; Pop-Location; exit 1 }
Pop-Location

# 2. Close Rhino (exact name "Rhino": a "Rhino*" wildcard would also match helper
# processes such as rhino-mcp-router and drop the MCP connection)
$rhino = Get-Process -Name "Rhino" -ErrorAction SilentlyContinue
$stateFile = Join-Path $env:TEMP "rhino-last-gh.txt"
if ($rhino) {
    # Remember the open .gh file: the window title ends with "<name>.gh"; the full
    # path is looked up with Everything (es.exe) if it is available.
    $gh = $rhino | Where-Object { $_.MainWindowTitle -match '([^\/ ][^\/]*\.gh)\s*$' } | Select-Object -First 1
    if ($gh -and $gh.MainWindowTitle -match '([^\/ ][^\/]*\.gh)\s*$' -and (Get-Command es.exe -ErrorAction SilentlyContinue)) {
        $name = $Matches[1]
        $path = (& es.exe -n 1 -sort date-modified-descending "`"$name`"" 2>$null | Select-Object -First 1)
        if ($path) { Set-Content -Path $stateFile -Value $path -Encoding UTF8; Write-Host "Last file saved: $path" }
    }
    Write-Host "Closing Rhino..."
    $rhino | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# 3. C# build + deploy to Grasshopper Libraries
Write-Host "Building Slate.gha..."
dotnet build "$root\src\Slate.csproj"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Reopening Rhino (MCP starts automatically)..."
    # MCPStart asks for confirmation; '_Enter' answers it. Opening the .gh file from
    # the startup script does not work (Grasshopper is not ready yet), so it is
    # opened afterwards, once Rhino is up.
    Start-Process "C:\Program Files\Rhino 8\System\Rhino.exe" -ArgumentList '/nosplash', '/runscript="_MCPStart _Enter"'
    if (Test-Path $stateFile) { Write-Host "Last file: $(Get-Content $stateFile)" }
} else {
    Write-Host "C# build failed."
}
