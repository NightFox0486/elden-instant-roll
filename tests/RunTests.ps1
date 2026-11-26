$ErrorActionPreference = "Stop"

$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $cscPath)) {
    $cscPath = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

$rootDir = Split-Path -Parent $PSScriptRoot
$srcDir = Join-Path $rootDir "src"
$testExe = Join-Path $env:TEMP "EldenInstantRoll.Tests.exe"

$sources = @(
    (Join-Path $srcDir "NativeMethods.cs"),
    (Join-Path $srcDir "KeyBinding.cs"),
    (Join-Path $srcDir "AppSettings.cs"),
    (Join-Path $srcDir "StartupRegistryManager.cs"),
    (Join-Path $srcDir "AppController.cs"),
    (Join-Path $PSScriptRoot "StateMachineTests.cs")
)

& $cscPath /nologo /target:exe /platform:anycpu /utf8output /warn:4 /out:$testExe /reference:System.dll @sources
if ($LASTEXITCODE -ne 0) {
    throw "Test compilation failed."
}

try {
    & $testExe
    if ($LASTEXITCODE -ne 0) {
        throw "Automated tests failed with exit code $LASTEXITCODE"
    }
} finally {
    if (Test-Path $testExe) {
        Remove-Item $testExe -Force -ErrorAction SilentlyContinue
    }
}
