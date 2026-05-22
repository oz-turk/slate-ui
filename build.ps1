$root = $PSScriptRoot

# 1. Svelte build → src/Resources/
Write-Host "Building Svelte UI..."
Push-Location "$root\ui"
npm run build
if ($LASTEXITCODE -ne 0) { Write-Host "Svelte build failed."; Pop-Location; exit 1 }
Pop-Location

# 2. Close Rhino
$rhino = Get-Process -Name "Rhino*" -ErrorAction SilentlyContinue
if ($rhino) {
    Write-Host "Closing Rhino..."
    $rhino | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# 3. C# build + deploy to Grasshopper Libraries
Write-Host "Building Slate.gha..."
dotnet build "$root\src\Slate.csproj"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Reopening Rhino..."
    Start-Process "C:\Program Files\Rhino 8\System\Rhino.exe"
} else {
    Write-Host "C# build failed."
}
