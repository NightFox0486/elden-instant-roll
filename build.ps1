[CmdletBinding()]
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot "dist"),
    [string]$OutputName = "EldenInstantRoll.exe"
)

$ErrorActionPreference = "Stop"

$cscCandidates = @(
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

$cscPath = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $cscPath) {
    throw "Could not find .NET Framework 4.x csc.exe."
}

$srcDir = Join-Path $PSScriptRoot "src"
$sourceFiles = Get-ChildItem -Path $srcDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName }
if (-not $sourceFiles -or $sourceFiles.Count -eq 0) {
    throw "No C# source files found in $srcDir"
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$outputExe = Join-Path $OutputDir $OutputName
$tempIcon = Join-Path $OutputDir "EldenInstantRoll_build.ico"

# Generate crisp embedded .ico (PNG-in-ICO 64x64) so Windows Explorer and shortcuts display a custom icon
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap(64, 64)
try {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        $bgBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(24, 26, 31))
        $g.FillEllipse($bgBrush, 3, 3, 58, 58)
        $bgBrush.Dispose()

        $outerPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(212, 168, 67), 5.0)
        $g.DrawEllipse($outerPen, 6, 6, 52, 52)
        $outerPen.Dispose()

        $innerPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(16, 185, 129), 3.2)
        $g.DrawEllipse($innerPen, 16, 16, 32, 32)
        $innerPen.Dispose()

        $spearPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(212, 168, 67), 3.4)
        $spearPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $spearPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLine($spearPen, 32, 10, 32, 54)
        $spearPen.Dispose()
    } finally {
        $g.Dispose()
    }

    $pngStream = New-Object System.IO.MemoryStream
    try {
        $bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngBytes = $pngStream.ToArray()
    } finally {
        $pngStream.Dispose()
    }

    $icoStream = [System.IO.File]::Create($tempIcon)
    $bw = New-Object System.IO.BinaryWriter($icoStream)
    try {
        # ICONDIR header (6 bytes)
        $bw.Write([UInt16]0)  # Reserved
        $bw.Write([UInt16]1)  # Type: 1 = ICO
        $bw.Write([UInt16]1)  # Image count
        # ICONDIRENTRY (16 bytes)
        $bw.Write([Byte]64)   # Width
        $bw.Write([Byte]64)   # Height
        $bw.Write([Byte]0)    # Color count
        $bw.Write([Byte]0)    # Reserved
        $bw.Write([UInt16]1)  # Color planes
        $bw.Write([UInt16]32) # Bits per pixel
        $bw.Write([UInt32]$pngBytes.Length)
        $bw.Write([UInt32]22) # Offset = 6 + 16
        # PNG payload
        $bw.Write($pngBytes)
    } finally {
        $bw.Dispose()
        $icoStream.Dispose()
    }
} finally {
    $bmp.Dispose()
}

Write-Host "Building $outputExe using $cscPath ..." -ForegroundColor Cyan

$cscArgs = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/platform:anycpu",
    "/utf8output",
    "/warn:4",
    "/out:$outputExe",
    "/win32icon:$tempIcon",
    "/reference:System.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Windows.Forms.dll"
) + $sourceFiles

try {
    & $cscPath @cscArgs
    if ($LASTEXITCODE -ne 0) {
        throw "csc.exe failed with exit code $LASTEXITCODE"
    }
} finally {
    if (Test-Path $tempIcon) {
        Remove-Item $tempIcon -Force -ErrorAction SilentlyContinue
    }
}

# Verify standalone dependency footprint (only built-in .NET Framework 4.x GAC assemblies)
$asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($outputExe)
$refs = $asm.GetReferencedAssemblies()
$allowedBuiltins = @("mscorlib", "System", "System.Core", "System.Drawing", "System.Windows.Forms")
$unexpected = $refs | Where-Object { $allowedBuiltins -notcontains $_.Name }

$fileInfo = Get-Item $outputExe
Write-Host ("Built successfully: {0} ({1:N0} bytes)" -f $fileInfo.FullName, $fileInfo.Length) -ForegroundColor Green
Write-Host ("Referenced assemblies: " + (($refs | ForEach-Object { $_.Name }) -join ", "))

if ($unexpected) {
    throw ("Unexpected external assembly dependency detected: " + (($unexpected | ForEach-Object { $_.Name }) -join ", "))
}
Write-Host "Standalone check passed: zero external DLL dependencies." -ForegroundColor Green
