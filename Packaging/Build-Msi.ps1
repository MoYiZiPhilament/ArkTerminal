#requires -Version 7.4
[CmdletBinding()]
param([string]$PortableZip, [string]$WixPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.InformationalVersion
$fileVersion = [version]$props.Project.PropertyGroup.FileVersion
$msiVersion = $fileVersion.ToString(3)
$output = Join-Path $repo "bin\Packages\$version"
if (!$PortableZip) { $PortableZip = Join-Path $output "ArkTerminal-$version-win-x64-portable.zip" }
if (!$WixPath) { $WixPath = Join-Path $repo 'obj\Tools\wix4\wix.exe' }
$PortableZip = (Resolve-Path -LiteralPath $PortableZip).Path
$WixPath = (Resolve-Path -LiteralPath $WixPath).Path
if ((& $WixPath --version) -notlike '4.0.6*') { throw 'This installer requires WiX 4.0.6.' }
$msi = Join-Path $output "ArkTerminal-$version-win-x64.msi"
if (Test-Path -LiteralPath $msi) { throw "Output already exists: $msi" }
$work = Join-Path $repo ('obj\Packaging\' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'payload'
New-Item -ItemType Directory -Path $work -Force | Out-Null

function Escape-Xml([string]$Value) { [Security.SecurityElement]::Escape($Value) }
function Get-Id([string]$Path) { 'P' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Path))).Substring(0, 24) }
$components = [Collections.Generic.List[string]]::new()
function Write-Payload([string]$Directory) {
    $relative = [IO.Path]::GetRelativePath($payload, $Directory)
    $id = Get-Id $relative
    $components.Add($id)
    $guidBytes = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("ArkTerminal.Msi.Payload/$relative"))
    $guid = [guid]::new([byte[]]$guidBytes[0..15])
    "<Component Id=`"$id`" Guid=`"$guid`">"
    "<RegistryValue Root=`"HKCU`" Key=`"Software\ArkTerminal\Installer\Components`" Name=`"$id`" Type=`"integer`" Value=`"1`" KeyPath=`"yes`" />"
    "<RemoveFolder Id=`"Remove$id`" On=`"uninstall`" />"
    foreach ($file in Get-ChildItem -LiteralPath $Directory -File | Sort-Object Name) {
        "<File Id=`"$(Get-Id ([IO.Path]::GetRelativePath($payload, $file.FullName)))`" Source=`"$(Escape-Xml $file.FullName)`" />"
    }
    '</Component>'
    foreach ($child in Get-ChildItem -LiteralPath $Directory -Directory | Sort-Object Name) {
        "<Directory Id=`"D$(Get-Id ([IO.Path]::GetRelativePath($payload, $child.FullName)))`" Name=`"$(Escape-Xml $child.Name)`">"
        Write-Payload $child.FullName
        '</Directory>'
    }
}

try {
    [IO.Compression.ZipFile]::ExtractToDirectory($PortableZip, $payload)
    $infoPath = Join-Path $payload 'distribution.json'
    $info = Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json -AsHashtable
    if ($info.version -ne $version -or [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload 'ArkTerminal.exe')).ProductVersion -ne $version) { throw 'Portable package version does not match.' }
    if (@(Get-ChildItem -LiteralPath (Join-Path $payload 'data') -Force).Count) { throw 'Portable package contains user data.' }
    Remove-Item -LiteralPath (Join-Path $payload 'portable.flag')
    Remove-Item -LiteralPath (Join-Path $payload 'data')
    $info['msiSignature'] = 'unsigned'
    $info['msiScope'] = 'perUser'
    $info['msiProductVersion'] = $msiVersion
    $info['msiInstalledValidation'] = $false
    $info['dataMode'] = 'standard'
    $info | ConvertTo-Json | Set-Content -LiteralPath $infoPath -Encoding utf8NoBOM
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $payload 'DISTRIBUTION.md')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MSI-NOTICES.md') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WiX-LICENSE.txt') -Destination (Join-Path $payload 'Licenses\WiX-LICENSE.txt')
    $wixSource = Join-Path $repo 'obj\Tools\WiX-4.0.6-source.zip'
    if (!(Test-Path -LiteralPath $wixSource)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $wixSource) -Force | Out-Null
        Invoke-WebRequest -Uri 'https://codeload.github.com/wixtoolset/wix/zip/73c897383236ddbbbc6ba257634013c1269ceec2' -OutFile $wixSource
    }
    if ((Get-FileHash -LiteralPath $wixSource -Algorithm SHA256).Hash -ne 'BA8C6799BCFCAA3B475F8A94251F35B602EB475AD563ED5EE205ED5BE84443CE') { throw 'WiX source archive hash mismatch.' }
    Copy-Item -LiteralPath $wixSource -Destination (Join-Path $payload 'Licenses\WiX-4.0.6-source.zip')
    $license = [IO.File]::ReadAllText((Join-Path $repo 'LICENSE')).Replace('\', '\\').Replace('{', '\{').Replace('}', '\}').Replace("`r", '').Replace("`n", '\par ')
    [IO.File]::WriteAllText((Join-Path $work 'License.rtf'), '{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\f0\fs18 ' + $license + '}')
    $xml = @('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Fragment><DirectoryRef Id="INSTALLFOLDER">')
    $xml += @(Write-Payload $payload)
    $xml += '</DirectoryRef><ComponentGroup Id="Payload">'
    $xml += $components | ForEach-Object { "<ComponentRef Id=`"$_`" />" }
    $xml += '</ComponentGroup></Fragment></Wix>'
    $xml | Set-Content -LiteralPath (Join-Path $work 'Payload.wxs') -Encoding utf8NoBOM
    Push-Location $repo
    try {
        & $WixPath build (Join-Path $PSScriptRoot 'Installer.wxs') (Join-Path $work 'Payload.wxs') -arch x64 -ext WixToolset.UI.wixext/4.0.6 -d "DisplayVersion=$version" -d "MsiVersion=$msiVersion" -d "AppIcon=$(Join-Path $repo 'Assets\AppIcon.ico')" -d "LicenseRtf=$(Join-Path $work 'License.rtf')" -pdbtype none -intermediatefolder (Join-Path $work 'wix') -out $msi
        if ($LASTEXITCODE -ne 0) { throw "WiX build failed: $LASTEXITCODE" }
        # ICE91 warns about per-user paths even for a deliberately per-user-only package.
        & $WixPath msi validate $msi -sice ICE91
        if ($LASTEXITCODE -ne 0) { throw "MSI validation failed: $LASTEXITCODE" }
    } finally { Pop-Location }
    $hash = (Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($msi))" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS-msi.txt') -Encoding ascii
    Write-Output "MSI: $msi (per-user, unsigned; installed acceptance pending)"
} finally {
    $stagingParent = [IO.Path]::GetFullPath((Join-Path $repo 'obj\Packaging'))
    if ((Split-Path -Parent ([IO.Path]::GetFullPath($work))) -eq $stagingParent -and (Test-Path -LiteralPath $work)) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
