# Builds a Release .gha and repackages it into a .yak, including every
# runtime dependency (WebView2 + System.Text.Json's chain) alongside it.
# A .yak with just Slate.gha reproduces the "System.Text.Json ... could not
# load" crash Rhino 7 hit on 2026-08-26, since those DLLs aren't guaranteed
# to exist anywhere else on a fresh machine. Run with Rhino closed.

$root   = $PSScriptRoot
$yakDir = "$root\yak"
$outDir = "$root\src\bin\Release\net48"

Write-Host "Building Slate.gha (Release)..."
# Ignore exit code: the csproj's own DeployToGrasshopper copy step fails with
# a lock error whenever Rhino is open, which is unrelated to compilation.
# Check the actual output file below instead of trusting $LASTEXITCODE.
dotnet build "$root\src\Slate.csproj" -c Release | Out-Host
if (-not (Test-Path "$outDir\Slate.gha")) { Write-Host "Build failed - no Slate.gha produced."; exit 1 }

Write-Host "Copying .gha + dependency DLLs into yak/..."
Get-ChildItem "$yakDir\*.gha", "$yakDir\*.dll", "$yakDir\*.yak" -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item "$outDir\Slate.gha" $yakDir
Copy-Item "$outDir\*.dll" $yakDir
Copy-Item "$outDir\runtimes\win-x64\native\WebView2Loader.dll" $yakDir

Write-Host "Running yak build..."
Push-Location $yakDir
& "C:\Program Files\Rhino 8\System\Yak.exe" build --platform win
Pop-Location
