#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WindowsSdkBin,
    [string]$OutputDirectory,
    [switch]$DevelopmentSign
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent $PSScriptRoot
[xml]$versionProps = Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw
$version = [string]$versionProps.Project.PropertyGroup.InformationalVersion
$packageVersion = [string]$versionProps.Project.PropertyGroup.FileVersion
$runtimeVersion = [string]$versionProps.Project.PropertyGroup.RuntimeFrameworkVersion
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo "bin\Packages\$version" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$makeAppx = Join-Path $WindowsSdkBin 'makeappx.exe'
$signTool = Join-Path $WindowsSdkBin 'signtool.exe'
foreach ($tool in @($makeAppx, $signTool)) { if (!(Test-Path -LiteralPath $tool)) { throw "Windows SDK tool missing: $tool" } }
$zipPath = Join-Path $output "ArkTerminal-$version-win-x64-portable.zip"
$signature = if ($DevelopmentSign) { 'development-self-signed-untrusted' } else { 'unsigned' }
$suffix = if ($DevelopmentSign) { 'development' } else { 'unsigned' }
$msixPath = Join-Path $output "ArkTerminal-$version-x64.$suffix.msix"
foreach ($path in @($zipPath, $msixPath)) { if (Test-Path -LiteralPath $path) { throw "Output already exists; choose a new -OutputDirectory: $path" } }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$work = Join-Path $repo ('obj\Packaging\' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $work 'publish'
$portable = Join-Path $work 'portable'
$msix = Join-Path $work 'msix'

function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable exited with code $LASTEXITCODE" }
}

function Add-Licenses([string]$Destination) {
    $licenseRoot = Join-Path $Destination 'Licenses'
    New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $Destination 'LICENSE')
    Copy-Item -Path (Join-Path $PSScriptRoot 'Licenses\*') -Destination $licenseRoot -Recurse
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $Destination
    $packages = @{}
    $packageFolders = @()
    foreach ($project in @('obj\project.assets.json', 'ArkTerminal.Mcp\obj\project.assets.json')) {
        $assets = Get-Content -LiteralPath (Join-Path $repo $project) -Raw | ConvertFrom-Json -AsHashtable
        $packageFolders += $assets.packageFolders.Keys
        foreach ($library in $assets.libraries.GetEnumerator() | Where-Object { $_.Value.type -eq 'package' }) {
            $packageDir = $assets.packageFolders.Keys | ForEach-Object { Join-Path $_ $library.Value.path } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
            [xml]$spec = Get-Content -LiteralPath (Get-ChildItem -LiteralPath $packageDir -Filter '*.nuspec' | Select-Object -First 1).FullName -Raw
            if ([string]$spec.package.metadata.license.InnerText -notin @('MIT', 'Apache-2.0')) { throw "Review license for $($library.Key) before packaging" }
            $packages[$library.Key] = [ordered]@{ package = $library.Key; license = $spec.package.metadata.license.InnerText; sha512 = $library.Value.sha512 }
            $notices = @(Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -match '^(LICENSE|THIRD.PARTY|NOTICE)' })
            if ($notices.Count) {
                $noticeDir = Join-Path $licenseRoot ($library.Key.Replace('/', '-'))
                New-Item -ItemType Directory -Path $noticeDir -Force | Out-Null
                $notices | Copy-Item -Destination $noticeDir
            }
        }
    }
    foreach ($runtime in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
        $runtimeDir = $packageFolders | Select-Object -Unique | ForEach-Object { Join-Path $_ "$runtime\$runtimeVersion" } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (!$runtimeDir) { throw "Runtime license directory missing: $runtime/$runtimeVersion" }
        $dest = Join-Path $licenseRoot "$runtime-$runtimeVersion"
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
        $runtimeNotices = @(Get-ChildItem -LiteralPath $runtimeDir -File | Where-Object { $_.Name -match '^(LICENSE|THIRD.PARTY)' })
        if (!($runtimeNotices | Where-Object { $_.Name -match '^LICENSE' })) { throw "Runtime license missing: $runtime" }
        $runtimeNotices | Copy-Item -Destination $dest
    }
    @($packages.Values | Sort-Object package) | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $licenseRoot 'packages.json') -Encoding utf8NoBOM
}

function New-PackageLogos([string]$Destination) {
    Add-Type -AssemblyName System.Drawing.Common
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $source = [Drawing.Image]::FromFile((Join-Path $repo 'Assets\AppIcon.png'))
    try {
        foreach ($logo in @(@('StoreLogo.png', 50), @('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150))) {
            $size = [int]$logo[1]
            $bitmap = [Drawing.Bitmap]::new($size, $size)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($source, 0, 0, $size, $size)
                $bitmap.Save((Join-Path $Destination $logo[0]), [Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    } finally { $source.Dispose() }
}

try {
    Invoke-Checked 'dotnet' @('publish', (Join-Path $repo 'ArkTerminal.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:RestoreLockedMode=true', "-p:PublishDir=$publish\", '-v:minimal')
    foreach ($relative in @('ArkTerminal.exe', 'coreclr.dll', 'PresentationNative_cor3.dll', 'Mcp\ArkTerminal.Mcp.exe', 'Mcp\coreclr.dll', 'Mcp\ModelContextProtocol.Core.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $publish $relative))) { throw "Incomplete self-contained publish: $relative" }
    }
    Add-Licenses $publish
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $publish 'DISTRIBUTION.md')
    $info = [ordered]@{ version = $version; packageVersion = $packageVersion; runtime = "win-x64 self-contained .NET $runtimeVersion"; minimumWindowsBuild = 17763; minimumWindowsValidated = $false; msixSignature = $signature; portableExecutableSignature = 'unsigned'; msixInstalledValidation = $false; msixMcp = 'unavailable-pending-installed-validation'; msixElevation = 'unavailable-pending-installed-validation'; sdk = (& dotnet --version); windowsSdk = (Get-Item -LiteralPath $makeAppx).VersionInfo.FileVersion; createdUtc = [DateTime]::UtcNow.ToString('o') }
    $info | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publish 'distribution.json') -Encoding utf8NoBOM
    Copy-Item -LiteralPath $publish -Destination $portable -Recurse
    [IO.File]::WriteAllText((Join-Path $portable 'portable.flag'), '')
    New-Item -ItemType Directory -Path (Join-Path $portable 'data') | Out-Null
    [IO.Compression.ZipFile]::CreateFromDirectory($portable, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)

    Copy-Item -LiteralPath $publish -Destination $msix -Recurse
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
    $manifest.Package.Identity.Version = $packageVersion
    $manifest.Save((Join-Path $msix 'AppxManifest.xml'))
    New-PackageLogos (Join-Path $msix 'Assets')
    Invoke-Checked $makeAppx @('pack', '/d', $msix, '/p', $msixPath, '/o', '/h', 'SHA256')
    if ($DevelopmentSign) {
        $rsa = [Security.Cryptography.RSA]::Create(2048)
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=ArkTerminal Local Development', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true))
        $oids = [Security.Cryptography.OidCollection]::new()
        [void]$oids.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids, $false))
        $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddDays(30))
        $password = [guid]::NewGuid().ToString('N')
        $pfx = Join-Path $work 'ephemeral-development.pfx'
        try {
            [IO.File]::WriteAllBytes($pfx, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $password))
            [IO.File]::WriteAllBytes((Join-Path $output 'ArkTerminal-Development.cer'), $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
            Invoke-Checked $signTool @('sign', '/fd', 'SHA256', '/f', $pfx, '/p', $password, $msixPath)
            $info['developmentCertificateThumbprint'] = $certificate.Thumbprint
        } finally {
            if (Test-Path -LiteralPath $pfx) { Remove-Item -LiteralPath $pfx -Force }
            $certificate.Dispose(); $rsa.Dispose()
        }
    }
    $info | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'distribution.json') -Encoding utf8NoBOM
    Get-FileHash -LiteralPath $zipPath, $msixPath -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_.Path) } | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
    Write-Output "Local packages: $output ($signature; installed MSIX acceptance pending)"
} finally {
    # Only this invocation's unique staging directory can be removed.
    $stagingParent = [IO.Path]::GetFullPath((Join-Path $repo 'obj\Packaging'))
    if ((Split-Path -Parent ([IO.Path]::GetFullPath($work))) -eq $stagingParent -and (Test-Path -LiteralPath $work)) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
