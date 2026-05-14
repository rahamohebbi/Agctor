# Generate JPEG diagrams from *.mmd in a docs directory (Windows).
# Requires: npx @mermaid-js/mermaid-cli (mmdc) on PATH via npx
# Uses System.Drawing to convert PNG (mmdc output) to JPEG.
param(
    [Parameter(Mandatory = $true)]
    [string]$DocsDir
)

$ErrorActionPreference = "Stop"
$resolved = if ([System.IO.Path]::IsPathRooted($DocsDir)) { $DocsDir } else { Join-Path (Split-Path $PSScriptRoot -Parent) $DocsDir }
if (-not (Test-Path $resolved)) { throw "Directory not found: $resolved" }

Add-Type -AssemblyName System.Drawing

Push-Location $resolved
try {
    foreach ($mmd in Get-ChildItem -Filter *.mmd) {
        $base = [System.IO.Path]::GetFileNameWithoutExtension($mmd.Name)
        $tmpPng = Join-Path $resolved ($base + ".__tmp__.png")
        $jpg = Join-Path $resolved ($base + ".jpg")
        Write-Host "Rendering $($mmd.Name) -> $jpg"
        npx -y @mermaid-js/mermaid-cli -i $mmd.FullName -o $tmpPng -b white -w 2400 -H 1800 -s 2
        $img = [System.Drawing.Image]::FromFile($tmpPng)
        try {
            $jpgCodec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq "image/jpeg" }
            $encParams = New-Object System.Drawing.Imaging.EncoderParameters 1
            $encParams.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality, 90L)
            $img.Save($jpg, $jpgCodec, $encParams)
        }
        finally {
            $img.Dispose()
            Remove-Item -Force $tmpPng
        }
    }
}
finally {
    Pop-Location
}

Write-Host "Done: $resolved"
