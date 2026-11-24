param(
    [string]$Version = "3.0.0"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $Root

$DistDir = Join-Path $Root "dist-release"
$ObjDir = Join-Path $Root "obj"
$BundleZip = Join-Path $ObjDir "wwwroot.bundle.zip"
$PublishStandaloneDir = Join-Path $ObjDir "publish-standalone"
$PublishAppDir = Join-Path $ObjDir "publish-app"
$PublishSetupDir = Join-Path $ObjDir "publish-setup"
$SetupPayloadZip = Join-Path $Root "tools\NeuralRigSetup\payload.zip"

Write-Host "[1/6] Stopping active NeuralRig processes..."
Stop-Process -Name "NeuralRigStudio", "NeuralRigMcp" -Force -ErrorAction SilentlyContinue

Write-Host "[2/6] Building Web UI bundle (web-ui/ -> wwwroot/)..."
npm --prefix (Join-Path $Root "web-ui") run build
if ($LASTEXITCODE -ne 0) { throw "Web UI build failed." }

New-Item -ItemType Directory -Force -Path $DistDir, $ObjDir | Out-Null
if (Test-Path $BundleZip) { Remove-Item $BundleZip -Force }
Compress-Archive -Path (Join-Path $Root "wwwroot\*") -DestinationPath $BundleZip -Force

Write-Host "[3/6] Publishing Self-Contained Single-File EXE..."
if (Test-Path $PublishStandaloneDir) { Remove-Item $PublishStandaloneDir -Recurse -Force }
dotnet publish (Join-Path $Root "NeuralRigStudio.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:EmbedWwwRootBundle=true `
    -p:Version=$Version `
    -o $PublishStandaloneDir
if ($LASTEXITCODE -ne 0) { throw "Standalone single-file publish failed." }

$StandaloneOut = Join-Path $DistDir "NeuralRigStudio-v$Version-win-x64-standalone.exe"
Copy-Item (Join-Path $PublishStandaloneDir "NeuralRigStudio.exe") $StandaloneOut -Force

Write-Host "[4/6] Publishing Full Application Payload (Studio + NeuralRigMcp)..."
if (Test-Path $PublishAppDir) { Remove-Item $PublishAppDir -Recurse -Force }
dotnet publish (Join-Path $Root "NeuralRigStudio.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version `
    -o $PublishAppDir
if ($LASTEXITCODE -ne 0) { throw "App publish failed." }

dotnet publish (Join-Path $Root "tools\NeuralRigMcp\NeuralRigMcp.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version `
    -o $PublishAppDir
if ($LASTEXITCODE -ne 0) { throw "NeuralRigMcp publish failed." }

Write-Host "[5/6] Building Installer (Setup.exe)..."
$SetupOut = Join-Path $DistDir "NeuralRigStudio-v$Version-Setup-win-x64.exe"
$Iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $Iscc -and (Test-Path "C:\Program Files (x86)\Inno Setup 6\ISCC.exe")) {
    $Iscc = @{ Source = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" }
}

if ($Iscc) {
    & $Iscc.Source "/DAppVersion=$Version" (Join-Path $Root "installer\NeuralRigStudio.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup build failed." }
} else {
    Write-Host "  ISCC.exe not found -> building self-extracting .NET Setup.exe via tools/NeuralRigSetup..."
    if (Test-Path $SetupPayloadZip) { Remove-Item $SetupPayloadZip -Force }
    Compress-Archive -Path (Join-Path $PublishAppDir "*") -DestinationPath $SetupPayloadZip -Force
    try {
        if (Test-Path $PublishSetupDir) { Remove-Item $PublishSetupDir -Recurse -Force }
        dotnet publish (Join-Path $Root "tools\NeuralRigSetup\NeuralRigSetup.csproj") `
            -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:EnableCompressionInSingleFile=true `
            -p:Version=$Version `
            -o $PublishSetupDir
        if ($LASTEXITCODE -ne 0) { throw "NeuralRigSetup publish failed." }
        Copy-Item (Join-Path $PublishSetupDir "NeuralRigSetup.exe") $SetupOut -Force
    }
    finally {
        if (Test-Path $SetupPayloadZip) { Remove-Item $SetupPayloadZip -Force }
    }
}

Write-Host "[6/6] Writing SHA256SUMS.txt..."
$SumFile = Join-Path $DistDir "SHA256SUMS.txt"
$Lines = @($SetupOut, $StandaloneOut) | ForEach-Object {
    $h = (Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant()
    "$h  $(Split-Path -Leaf $_)"
}
$Lines | Set-Content -Path $SumFile -Encoding UTF8

Write-Host "[PASS] Release artifacts ready in dist-release/:"
Get-ChildItem $DistDir | Format-Table Name, Length -AutoSize
