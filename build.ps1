# Builds the plugin and creates the release zip in dist\.
# The zip's contents go straight into MusicBee's Plugins folder.
#
# Everything comes from this repository and NuGet: the romaji engine (RomajiCore.dll, built from
# external\RomajiCore), LibNMeCab.dll and the IPAdic dictionary (NuGet LibNMeCab / LibNMeCab.IpaDicBin).
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$info = Get-Content (Join-Path $root 'src\DesktopLyrics\Properties\AssemblyInfo.cs') -Raw
$version = ([regex]'AssemblyVersion\("(\d+\.\d+\.\d+)').Match($info).Groups[1].Value

dotnet build (Join-Path $root 'src\DesktopLyrics\DesktopLyrics.csproj') -c $Configuration -nologo
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

$bin = Join-Path $root "src\DesktopLyrics\bin\$Configuration\net48"
foreach ($required in 'mb_DesktopLyrics.dll', 'RomajiCore.dll', 'LibNMeCab.dll', 'IpaDic\sys.dic') {
    if (-not (Test-Path (Join-Path $bin $required))) { throw "Missing $required in $bin" }
}

$dist = Join-Path $root 'dist'
$stage = Join-Path $dist "MusicBee-DesktopLyrics-Romaji-$version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\DesktopLyrics\licenses", "$stage\RomajiLyrics\IpaDic", "$stage\RomajiLyrics\licenses" | Out-Null

Copy-Item "$bin\mb_DesktopLyrics.dll", "$bin\Newtonsoft.Json.dll", "$bin\Cyotek.Windows.Forms.ColorPicker.dll",
          "$bin\RomajiCore.dll", "$bin\LibNMeCab.dll" $stage
Copy-Item "$bin\IpaDic\*" "$stage\RomajiLyrics\IpaDic"

Copy-Item (Join-Path $root 'LICENSE') "$stage\DesktopLyrics\LICENSE.txt"
Copy-Item (Join-Path $root 'NOTICE') "$stage\DesktopLyrics\NOTICE.txt"
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') "$stage\DesktopLyrics\THIRD-PARTY-NOTICES.md"
Copy-Item (Join-Path $root 'licenses\*') "$stage\DesktopLyrics\licenses"

# The dictionary lives in RomajiLyrics\ because that is where RomajiCore looks for it, and the Romaji
# Lyrics plugin (if installed) uses the same copy. Keep its licences next to it.
Copy-Item (Join-Path $root 'licenses\RomajiCore-LICENSE.txt') "$stage\RomajiLyrics\LICENSE.txt"
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') "$stage\RomajiLyrics\THIRD-PARTY-NOTICES.md"
Copy-Item (Join-Path $root 'licenses\IPADIC-COPYING.txt'), (Join-Path $root 'licenses\NMeCab-COPYING.txt'),
          (Join-Path $root 'licenses\NMeCab-LGPL-2.1.txt') "$stage\RomajiLyrics\licenses"

@"
Bundled romaji data for Desktop Lyrics $version
https://github.com/nishizumi-maho/MusicBee-DesktopLyrics-Romaji

This folder holds the MeCab IPAdic dictionary that RomajiCore.dll (in the Plugins folder)
loads to convert Japanese lyrics into romaji. Desktop Lyrics looks for it here:

    <MusicBee plugins folder>\RomajiLyrics\IpaDic

Do not move or delete this folder unless you no longer want romaji.

RomajiCore is MIT-licensed by nishizumi-maho; see LICENSE.txt.
MeCab / NMeCab and IPADIC are third-party works under their own licences; see
THIRD-PARTY-NOTICES.md and the licences folder.
"@ | Set-Content "$stage\RomajiLyrics\README.txt" -Encoding UTF8

@"
Desktop Lyrics $version (romaji fork) for MusicBee
https://github.com/nishizumi-maho/MusicBee-DesktopLyrics-Romaji
Based on Desktop Lyrics by Charlie Jiang (https://github.com/cqjjjzr/MusicBee-DesktopLyrics),
Apache License 2.0.

Everything is included: the romaji engine and the IPAdic dictionary ship with this package.

INSTALL
1. Close MusicBee.
2. Copy everything in this zip into the MusicBee Plugins folder, keeping the folder
   structure (mb_DesktopLyrics.dll, RomajiCore.dll, LibNMeCab.dll, Newtonsoft.Json.dll,
   Cyotek.Windows.Forms.ColorPicker.dll, plus the DesktopLyrics and RomajiLyrics folders):
     installer version: %APPDATA%\MusicBee\Plugins
     portable version:  <MusicBee folder>\Plugins
3. Start MusicBee. Toggle the lyrics with View > Desktop Lyrics; right-click them for
   settings, the romaji switch and "Check for updates".

UPDATING
The plugin checks GitHub for new releases (you can turn this off in its settings). When
one is out it asks you, downloads it, verifies its SHA-256 checksum and installs it;
restart MusicBee to finish. To update by hand, close MusicBee and copy the new zip over
the old files.

UNINSTALL
Delete mb_DesktopLyrics.dll, RomajiCore.dll, LibNMeCab.dll and the DesktopLyrics and
RomajiLyrics folders. Newtonsoft.Json.dll and Cyotek.Windows.Forms.ColorPicker.dll may be
used by other plugins; delete them only if nothing else needs them.
Keep RomajiCore.dll, LibNMeCab.dll and RomajiLyrics if the Romaji Lyrics plugin is installed.
"@ | Set-Content "$stage\DesktopLyrics\README.txt" -Encoding UTF8

$zip = Join-Path $dist "MusicBee-DesktopLyrics-Romaji-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
# LF line ending, so `sha256sum -c` accepts the file as well.
[IO.File]::WriteAllText("$zip.sha256", "$hash  $(Split-Path $zip -Leaf)`n", [Text.Encoding]::ASCII)
Write-Host "Created $zip"
Write-Host "SHA256 $hash"
