[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$SkipWebUi,
    [switch]$SkipVerify
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $RepoRoot
try {
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "  NeuralRig Studio — VST3 v3.7 + WebView2 Native Builder" -ForegroundColor Cyan
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

    # 2) Publish .NET 10 NativeAOT Core DLL (NeuralRig.NativeCore.dll)
    $NativeCoreDir = Join-Path $RepoRoot "obj\vst3-nativecore"
    Write-Host "[2/6] Publishing .NET 10 NativeAOT NeuralRig.NativeCore.dll..." -ForegroundColor Yellow
    dotnet publish "src\NeuralRig.Plugin\NeuralRig.Plugin.csproj" -c Release -r win-x64 -p:PublishAot=true -p:NativeLib=Shared -o $NativeCoreDir
    if ($LASTEXITCODE -ne 0) { throw "NativeAOT publish failed ($LASTEXITCODE)" }

    $NativeCoreDll = Join-Path $NativeCoreDir "NeuralRig.NativeCore.dll"
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

    Write-Host "[3/6] Compiling C++20 VST3 COM + Win32 WebView2 shell..." -ForegroundColor Yellow
    $BundleRoot = Join-Path $RepoRoot "dist-vst3\NeuralRigStudio.vst3"
    $BundleBinDir = Join-Path $BundleRoot "Contents\x86_64-win"
    $BundleResDir = Join-Path $BundleRoot "Contents\Resources"
    $BundleWwwDir = Join-Path $BundleResDir "wwwroot"
    $ObjNativeDir = Join-Path $RepoRoot "obj\vst3-cpp"

    New-Item -ItemType Directory -Force -Path $BundleBinDir, $BundleResDir, $BundleWwwDir, $ObjNativeDir | Out-Null

    $Vst3Out = Join-Path $BundleBinDir "NeuralRigStudio.vst3"
    $CompileCmd = @"
call "$VcVars" >nul 2>&1
cl.exe /nologo /std:c++20 /O2 /EHsc /MD /DNDEBUG /DUNICODE /D_UNICODE /DNOMINMAX /I"$Wv2Include" /Fo"$ObjNativeDir\\" /LD "src\NeuralRig.Vst3Native\vst3_plugin.cpp" "src\NeuralRig.Vst3Native\vst3_webview_editor.cpp" "$Wv2Lib" user32.lib gdi32.lib ole32.lib oleaut32.lib shell32.lib shlwapi.lib advapi32.lib uuid.lib comdlg32.lib version.lib /link /DEF:"src\NeuralRig.Vst3Native\vst3_exports.def" /OUT:"$Vst3Out" /IMPLIB:"$ObjNativeDir\NeuralRigStudio.vst3.lib"
"@
    $CompileBat = Join-Path $ObjNativeDir "build_vst3.cmd"
    [System.IO.File]::WriteAllText($CompileBat, $CompileCmd, [System.Text.Encoding]::ASCII)
    cmd.exe /c "`"$CompileBat`""
    if ($LASTEXITCODE -ne 0) { throw "C++20 VST3 DLL compilation failed ($LASTEXITCODE)" }

    # 4) Assemble VST3 Bundle (NativeCore.dll + wwwroot + moduleinfo.json)
    Write-Host "[4/6] Assembling dist-vst3/NeuralRigStudio.vst3 bundle..." -ForegroundColor Yellow
    Copy-Item -Path $NativeCoreDll -Destination (Join-Path $BundleBinDir "NeuralRig.NativeCore.dll") -Force

    if (Test-Path $BundleWwwDir) { Remove-Item -Path $BundleWwwDir -Recurse -Force }
    Copy-Item -Path (Join-Path $RepoRoot "wwwroot") -Destination $BundleWwwDir -Recurse -Force

    $ModuleInfoJson = @'
{
  "Name": "NeuralRig Studio",
  "Version": "3.1.0",
  "Factory Info": {
    "Vendor": "NeuralRig Audio",
    "URL": "https://neuralrig.studio",
    "E-Mail": "support@neuralrig.studio",
    "Flags": {
      "Unicode": true,
      "Classes Discardable": false,
      "Component Non Discardable": false
    }
  },
  "Classes": [
    {
      "CID": "A73E91C452D84B6E91F0C38E4B72D101",
      "Category": "Audio Module Class",
      "Name": "NeuralRig Studio",
      "Vendor": "NeuralRig Audio",
      "Version": "3.1.0",
      "SDKVersion": "VST 3.7.11",
      "Sub Categories": [
        "Fx",
        "Guitar",
        "AmpSim"
      ],
      "Class Flags": 2,
      "Cardinality": 2147483647,
      "Snapshots": []
    }
  ]
}
'@
    [System.IO.File]::WriteAllText((Join-Path $BundleRoot "Contents\moduleinfo.json"), $ModuleInfoJson, (New-Object System.Text.UTF8Encoding($false)))

    # 5) Compile & Run Real Win32 / COM / WebView2 Host Verifier
    if (-not $SkipVerify) {
        Write-Host "[5/6] Building & running Vst3HostVerify.exe..." -ForegroundColor Yellow
        $VerifyObjDir = Join-Path $RepoRoot "obj\vst3-verify"
        New-Item -ItemType Directory -Force -Path $VerifyObjDir | Out-Null
        $VerifyExe = Join-Path $VerifyObjDir "Vst3HostVerify.exe"
        $VerifyCmd = @"
call "$VcVars" >nul 2>&1
cl.exe /nologo /std:c++20 /O2 /EHsc /MD /DNDEBUG /DUNICODE /D_UNICODE /DNOMINMAX /Fo"$VerifyObjDir\\" "tools\Vst3HostVerify\main.cpp" user32.lib ole32.lib /Fe:"$VerifyExe"
"@
        $VerifyBat = Join-Path $VerifyObjDir "build_verify.cmd"
        [System.IO.File]::WriteAllText($VerifyBat, $VerifyCmd, [System.Text.Encoding]::ASCII)
        cmd.exe /c "`"$VerifyBat`""
        if ($LASTEXITCODE -ne 0) { throw "Vst3HostVerify compilation failed ($LASTEXITCODE)" }

        & $VerifyExe $Vst3Out
        if ($LASTEXITCODE -ne 0) { throw "Vst3HostVerify verification failed ($LASTEXITCODE)" }
    } else {
        Write-Host "[5/6] Skipping Vst3HostVerify (-SkipVerify)" -ForegroundColor DarkGray
    }

    # 6) Optional System VST3 Installation
    if ($Install) {
        $SysVst3Dir = Join-Path $env:CommonProgramFiles "VST3\NeuralRigStudio.vst3"
        Write-Host "[6/6] Installing to $SysVst3Dir..." -ForegroundColor Yellow
        if (Test-Path $SysVst3Dir) { Remove-Item -Path $SysVst3Dir -Recurse -Force }
        Copy-Item -Path $BundleRoot -Destination $SysVst3Dir -Recurse -Force
    } else {
        Write-Host "[6/6] Bundle ready at $BundleRoot (pass -Install to copy to CommonProgramFiles\VST3)" -ForegroundColor Green
    }

    Write-Host "VST3 Build & Verification Complete!" -ForegroundColor Green
}
finally {
    Pop-Location
}
