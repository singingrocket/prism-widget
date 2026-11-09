<#
.SYNOPSIS
  Gives Prism.exe a package identity so it can read Windows notifications.

.DESCRIPTION
  1. Creates a self-signed code-signing certificate (CN=singingrocket) in your user certificate store, if missing.
  2. Trusts that certificate on this PC (Local Machine > Trusted People). This step needs administrator
     rights, so Windows shows a UAC prompt the first time.
  3. Builds and signs a small identity package from AppxManifest.xml.
  4. Registers the package for the current user, pointing at the folder that contains Prism.exe.

  Run it again whenever you move Prism.exe to another folder (for example after `dotnet publish`).

.PARAMETER ExeFolder
  Folder that contains Prism.exe. Defaults to the Debug build output.
#>
param(
    [string]$ExeFolder = (Join-Path $PSScriptRoot "..\bin\Debug\net10.0-windows10.0.22621.0\win-x64")
)

$ErrorActionPreference = "Stop"
$Subject = "CN=singingrocket"
$PackageName = "singingrocket.Prism"

$ExeFolder = (Resolve-Path $ExeFolder).Path
if (-not (Test-Path (Join-Path $ExeFolder "Prism.exe"))) { throw "Prism.exe not found in $ExeFolder. Build the project first." }

$tools = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Recurse -Include makeappx.exe, signtool.exe |
    Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending
$makeAppx = ($tools | Where-Object Name -eq "makeappx.exe" | Select-Object -First 1).FullName
$signTool = ($tools | Where-Object Name -eq "signtool.exe" | Select-Object -First 1).FullName
if (-not $makeAppx -or -not $signTool) { throw "makeappx/signtool not found. Run 'dotnet restore' in the Prism folder first." }

# 1. Certificate
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.Subject -eq $Subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and
    ($_.EnhancedKeyUsageList.ObjectId -contains "1.3.6.1.5.5.7.3.3")
} | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creating signing certificate $Subject ..."
    $cert = New-SelfSignedCertificate -Type Custom -Subject $Subject -KeyUsage DigitalSignature `
        -FriendlyName "Prism package signing" -CertStoreLocation Cert:\CurrentUser\My `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
}

# 2. Trust (admin)
$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $cert.Thumbprint
if (-not $trusted) {
    Write-Host "Trusting the certificate on this PC (approve the UAC prompt) ..."
    $cer = Join-Path $env:TEMP "prism-signing.cer"
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    $import = "Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
    Start-Process powershell -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList "-NoProfile", "-Command", $import
    Remove-Item $cer -ErrorAction SilentlyContinue
    if (-not (Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $cert.Thumbprint)) {
        throw "The certificate was not trusted (UAC prompt declined?)."
    }
}

# 3. Build and sign the identity package
$stage = Join-Path $env:TEMP "prism-package"
$out = Join-Path $PSScriptRoot "out"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $stage "Assets"), $out | Out-Null
Copy-Item (Join-Path $PSScriptRoot "AppxManifest.xml") $stage

Add-Type -AssemblyName System.Drawing
foreach ($logo in @(@("StoreLogo", 50), @("Square44x44Logo", 44), @("Square150x150Logo", 150))) {
    $size = $logo[1]
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(76, 120, 255)), ([System.Drawing.Color]::FromArgb(165, 110, 255)), 45
    $g.FillEllipse($brush, 0, 0, $size - 1, $size - 1)
    $font = New-Object System.Drawing.Font "Segoe UI Semibold", ($size * 0.45), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = "Center"; $format.LineAlignment = "Center"
    $g.DrawString("P", $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, 0, $size, $size), $format)
    $bmp.Save((Join-Path $stage "Assets\$($logo[0]).png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

$msix = Join-Path $out "Prism-identity.msix"
& $makeAppx pack /o /nv /d $stage /p $msix | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx failed ($LASTEXITCODE)." }
& $signTool sign /q /fd SHA256 /sha1 $cert.Thumbprint /s My $msix
if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)." }
Remove-Item $stage -Recurse -Force

# 4. Register for the current user
Get-Process Prism -ErrorAction SilentlyContinue | Stop-Process -Force
Get-AppxPackage $PackageName | Remove-AppxPackage
Add-AppxPackage -Path $msix -ExternalLocation $ExeFolder
Write-Host "Registered $PackageName for $ExeFolder. Start Prism.exe from that folder."
