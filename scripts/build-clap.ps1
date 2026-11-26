[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$SkipWebUi,
    [switch]$SkipVerify,
    [switch]$NoGuiVerify,
    [switch]$SkipNativeCore
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $RepoRoot
try {
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "  NeuralRig Studio - CLAP v1.2 + WebView2 Native Builder" -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan

    Stop-Process -Name NeuralRigStudio,NeuralRigMcp -Force -ErrorAction SilentlyContinue

    # 1) Build Web UI
    if (-not $SkipWebUi) {
        Write-Host "[1/6] Building React 19 UI (web-ui -> wwwroot)..." -ForegroundColor Yellow
        npm --prefix web-ui run build
        if ($LASTEXITCODE -ne 0) { throw "Web UI build failed ($LASTEXITCODE)" }
    } else {
        Write-Host "[1/6] Skipping Web UI build (-SkipWebUi)" -ForegroundColor DarkGray
    }

    # 2) Publish or reuse .NET 10 NativeAOT Core DLL (NeuralRig.NativeCore.dll)
    $NativeCoreDir = Join-Path $RepoRoot "obj\vst3-nativecore"
    $NativeCoreDll = Join-Path $NativeCoreDir "NeuralRig.NativeCore.dll"
    if ($SkipNativeCore -and (Test-Path $NativeCoreDll)) {
        Write-Host "[2/6] Reusing existing NativeAOT NeuralRig.NativeCore.dll (-SkipNativeCore)" -ForegroundColor DarkGray
    } else {
        Write-Host "[2/6] Publishing .NET 10 NativeAOT NeuralRig.NativeCore.dll..." -ForegroundColor Yellow
        dotnet publish "src\NeuralRig.Plugin\NeuralRig.Plugin.csproj" -c Release -r win-x64 -p:PublishAot=true -p:NativeLib=Shared -o $NativeCoreDir
        if ($LASTEXITCODE -ne 0) { throw "NativeAOT publish failed ($LASTEXITCODE)" }
    }
    if (-not (Test-Path $NativeCoreDll)) { throw "Missing published DLL: $NativeCoreDll" }

    # 3) Locate Visual Studio C++20 Toolchain & WebView2 Static SDK
    $VcVarsCandidates = @(
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat"
    )
    $VcVars = $VcVarsCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $VcVars) {
        $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
        if (Test-Path $vswhere) {
            $instPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
            if ($instPath) {
                $cand = Join-Path $instPath "VC\Auxiliary\Build\vcvars64.bat"
                if (Test-Path $cand) { $VcVars = $cand }
            }
        }
    }
    if (-not $VcVars) { throw "Could not find Visual Studio vcvars64.bat" }

    $Wv2PkgRoot = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.web.webview2"
    $Wv2VerDir = Get-ChildItem -Path $Wv2PkgRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    if (-not $Wv2VerDir) { throw "Could not find NuGet WebView2 SDK in $Wv2PkgRoot" }
    $Wv2Include = Join-Path $Wv2VerDir.FullName "build\native\include"
    $Wv2Lib = Join-Path $Wv2VerDir.FullName "build\native\x64\WebView2LoaderStatic.lib"
    if (-not (Test-Path $Wv2Lib)) { throw "Missing WebView2LoaderStatic.lib at $Wv2Lib" }

    $ClapInclude = Join-Path $RepoRoot "src\NeuralRig.ClapNative\clap\include"
    if (-not (Test-Path (Join-Path $ClapInclude "clap\clap.h"))) {
        throw "Missing CLAP C headers in $ClapInclude"
    }

    Write-Host "[3/6] Compiling C++20 CLAP v1.2 + Win32 WebView2 shell..." -ForegroundColor Yellow
    $DistClapDir = Join-Path $RepoRoot "dist-clap\NeuralRigStudio"
    $BundleWwwDir = Join-Path $DistClapDir "wwwroot"
    $ObjNativeDir = Join-Path $RepoRoot "obj\clap-cpp"

    New-Item -ItemType Directory -Force -Path $DistClapDir, $BundleWwwDir, $ObjNativeDir | Out-Null

    $ClapOut = Join-Path $DistClapDir "NeuralRigStudio.clap"
    $CompileCmd = @"
call "$VcVars" >nul 2>&1
cl.exe /nologo /std:c++20 /O2 /EHsc /MD /DNDEBUG /DUNICODE /D_UNICODE /DNOMINMAX /I"$ClapInclude" /I"$Wv2Include" /Fo"$ObjNativeDir\\" /LD "src\NeuralRig.ClapNative\clap_plugin.cpp" "src\NeuralRig.ClapNative\clap_webview_host.cpp" "$Wv2Lib" user32.lib gdi32.lib ole32.lib oleaut32.lib shell32.lib shlwapi.lib advapi32.lib uuid.lib comdlg32.lib version.lib /link /OUT:"$ClapOut" /IMPLIB:"$ObjNativeDir\NeuralRigStudio.clap.lib"
"@
    $CompileBat = Join-Path $ObjNativeDir "build_clap.cmd"
    [System.IO.File]::WriteAllText($CompileBat, $CompileCmd, [System.Text.Encoding]::ASCII)
    cmd.exe /c "`"$CompileBat`""
    if ($LASTEXITCODE -ne 0) { throw "C++20 CLAP DLL compilation failed ($LASTEXITCODE)" }

    # 4) Assemble dist-clap/NeuralRigStudio folder (NativeCore.dll + wwwroot)
    Write-Host "[4/6] Assembling dist-clap/NeuralRigStudio bundle..." -ForegroundColor Yellow
    Copy-Item -Path $NativeCoreDll -Destination (Join-Path $DistClapDir "NeuralRig.NativeCore.dll") -Force

    if (Test-Path $BundleWwwDir) { Remove-Item -Path $BundleWwwDir -Recurse -Force }
    Copy-Item -Path (Join-Path $RepoRoot "wwwroot") -Destination $BundleWwwDir -Recurse -Force

    # 5) Compile & Run Real Win32 / CLAP / WebView2 Host Verifier
    if (-not $SkipVerify) {
        Write-Host "[5/6] Building & running ClapHostVerify.exe..." -ForegroundColor Yellow
        $VerifyObjDir = Join-Path $RepoRoot "obj\clap-verify"
        New-Item -ItemType Directory -Force -Path $VerifyObjDir | Out-Null
        $VerifyExe = Join-Path $VerifyObjDir "ClapHostVerify.exe"
        $VerifyCmd = @"
call "$VcVars" >nul 2>&1
cl.exe /nologo /std:c++20 /O2 /EHsc /MD /DNDEBUG /DUNICODE /D_UNICODE /DNOMINMAX /I"$ClapInclude" /Fo"$VerifyObjDir\\" "tools\ClapHostVerify\main.cpp" user32.lib ole32.lib /Fe:"$VerifyExe"
"@
        $VerifyBat = Join-Path $VerifyObjDir "build_verify.cmd"
        [System.IO.File]::WriteAllText($VerifyBat, $VerifyCmd, [System.Text.Encoding]::ASCII)
        cmd.exe /c "`"$VerifyBat`""
        if ($LASTEXITCODE -ne 0) { throw "ClapHostVerify compilation failed ($LASTEXITCODE)" }

        if ($NoGuiVerify) {
            & $VerifyExe $ClapOut "--no-gui"
        } else {
            & $VerifyExe $ClapOut
        }
        if ($LASTEXITCODE -ne 0) { throw "ClapHostVerify verification failed ($LASTEXITCODE)" }
    } else {
        Write-Host "[5/6] Skipping ClapHostVerify (-SkipVerify)" -ForegroundColor DarkGray
    }

    # 6) Optional User-Scope CLAP Installation
    if ($Install) {
        $UserClapDir = Join-Path $env:LOCALAPPDATA "Programs\Common\CLAP\NeuralRigStudio"
        Write-Host "[6/6] Installing to $UserClapDir..." -ForegroundColor Yellow
        if (Test-Path $UserClapDir) { Remove-Item -Path $UserClapDir -Recurse -Force }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $UserClapDir) | Out-Null
        Copy-Item -Path $DistClapDir -Destination $UserClapDir -Recurse -Force
    } else {
        Write-Host "[6/6] Bundle ready at $DistClapDir (pass -Install to copy to LOCALAPPDATA\Programs\Common\CLAP)" -ForegroundColor Green
    }

    Write-Host "CLAP Build & Verification Complete!" -ForegroundColor Green
}
finally {
    Pop-Location
}
