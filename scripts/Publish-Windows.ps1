# Build a reviewable local portable package. No installation, signing or external publication.
param([switch] $FrameworkDependent)
$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $workspaceRoot 'src/Floe.Desktop/Floe.Desktop.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project
$version = [string]$projectXml.Project.PropertyGroup.Version
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$mode = if ($FrameworkDependent) { 'framework-dependent' } else { 'portable' }
$name = "Floe-$version-win-x64-$mode-$stamp"
$releaseRoot = Join-Path $workspaceRoot 'artifacts/releases'
$output = Join-Path $releaseRoot $name
New-Item -ItemType Directory -Path $output -Force | Out-Null
$arguments = @('publish',$project,'-c','Release','--no-restore','--output',$output,'/p:PublishProfile=Windows-x64')
if ($FrameworkDependent) { $arguments += '/p:SelfContained=false' }
& (Join-Path $PSScriptRoot 'dotnet.ps1') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed; incomplete files remain for diagnosis and were not packaged.' }
if (-not (Test-Path -LiteralPath (Join-Path $output 'Floe.exe'))) { throw 'Published executable is missing.' }
if (-not $FrameworkDependent -and -not (Test-Path -LiteralPath (Join-Path $output 'coreclr.dll'))) { throw 'Self-contained runtime is missing.' }
$instructions = @"
Floe $version - Windows x64

Extract the complete directory, then double-click Floe.exe.
Do not move only the EXE: its DLLs and native SQLite library must stay together.
Package type: $mode. Portable includes the .NET Desktop runtime; framework-dependent requires .NET Desktop Runtime 10 x64.

Data: %LOCALAPPDATA%\Floe\floe.db. No personal data is included in this package.
Close hides to tray by default. Choose Exit Floe from the tray or Settings to quit.
Before upgrading or backing up, exit Floe, then copy the entire data directory.
Replace application files only after exiting. Keep your data directory; do not reset it.
If startup is enabled and you move the application, disable/re-enable startup from the new location.
Logs, when errors occur: data directory\logs (two bounded files).
Windows 11 has native Acrylic; Windows 10 uses the visual fallback. This preview is unsigned.
"@
[IO.File]::WriteAllText((Join-Path $output 'README.txt'),$instructions,[Text.UTF8Encoding]::new($false))
$releaseNotes = Join-Path $workspaceRoot ('docs/releases/'+$version+'.md')
if (Test-Path -LiteralPath $releaseNotes) {
    Copy-Item -LiteralPath $releaseNotes -Destination (Join-Path $output 'ReleaseNotes.md')
}
$notices = Join-Path $output 'notices'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
$packages = Join-Path $workspaceRoot '.tools/nuget-packages'
foreach ($package in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64','communitytoolkit.mvvm','microsoft.data.sqlite.core','sqlitepclraw.bundle_e_sqlite3','sqlitepclraw.core','sqlitepclraw.provider.e_sqlite3','sqlitepclraw.lib.e_sqlite3')) {
    $packagePath = Join-Path $packages $package
    if (-not (Test-Path -LiteralPath $packagePath)) { continue }
    $packageVersion = Get-ChildItem -LiteralPath $packagePath -Directory | Sort-Object Name -Descending | Select-Object -First 1
    foreach ($file in Get-ChildItem -LiteralPath $packageVersion.FullName -File | Where-Object { $_.Name -match 'license|notice|\.nuspec$' }) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $notices ($package + '-' + $file.Name))
    }
}
$runtimeConfig = Get-Content -LiteralPath (Join-Path $output 'Floe.runtimeconfig.json') -Raw | ConvertFrom-Json
$frameworks = if ($FrameworkDependent) { $runtimeConfig.runtimeOptions.frameworks } else { $runtimeConfig.runtimeOptions.includedFrameworks }
$manifest = [pscustomobject]@{ Product='Floe'; Version=$version; RuntimeIdentifier='win-x64'; Mode=$mode; CreatedUtc=[DateTime]::UtcNow.ToString('o'); SchemaVersion=4; Signed=$false; Frameworks=$frameworks }
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'release.json') -Encoding UTF8
$checksums = Get-ChildItem -LiteralPath $output -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($output.Length+1).Replace('\','/')
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $relative
}
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'),[string[]]$checksums,[Text.UTF8Encoding]::new($false))
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $releaseRoot ($name+'.zip')
$archive = [IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $output -Recurse -File) {
        $relative = $file.FullName.Substring($output.Length+1).Replace('\','/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,($name+'/'+$relative),[IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($zip+'.sha256'),$hash+'  '+[IO.Path]::GetFileName($zip),[Text.UTF8Encoding]::new($false))
Write-Output ('Package: '+$zip)
Write-Output ('Executable: '+(Join-Path $output 'Floe.exe'))
Write-Output ('ZIP MiB: '+[Math]::Round((Get-Item -LiteralPath $zip).Length/1MB,2))
Write-Output ('SHA256: '+$hash)
