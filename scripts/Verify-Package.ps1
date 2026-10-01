# Validate local release files without launching the application or reading user data.
param(
    [Parameter(Mandatory = $true)] [string] $PackageDirectory,
    [string] $Archive
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PackageDirectory).Path.TrimEnd('\')
$listed = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $root 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw 'Malformed checksum entry.' }
    $expected = $matches[1]
    $relative = $matches[2]
    $file = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $file.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -or $listed.ContainsKey($relative)) { throw 'Invalid checksum path.' }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $expected) { throw ('Checksum mismatch: '+$relative) }
    $listed[$relative] = $true
}
foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
    $relative = $file.FullName.Substring($root.Length+1).Replace('\','/')
    if ($relative -ne 'SHA256SUMS.txt' -and -not $listed.ContainsKey($relative)) { throw ('Unlisted package file: '+$relative) }
}
$manifest = Get-Content -LiteralPath (Join-Path $root 'release.json') -Raw | ConvertFrom-Json
foreach ($name in @('Floe.exe','Floe.dll','e_sqlite3.dll','Floe.runtimeconfig.json','README.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $name))) { throw ('Required package file missing: '+$name) }
}
if ($manifest.Mode -eq 'portable') {
    foreach ($name in @('coreclr.dll','PresentationFramework.dll','hostfxr.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $name))) { throw ('Portable runtime file missing: '+$name) }
    }
}
if ($Archive) {
    $archivePath = (Resolve-Path -LiteralPath $Archive).Path
    $archiveHash = (Get-Content -LiteralPath ($archivePath+'.sha256') -Raw).Split(' ')[0]
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $archiveHash) { throw 'ZIP checksum mismatch.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entries = @($zip.Entries | Where-Object { $_.Name.Length -gt 0 })
        if ($entries.Count -ne $listed.Count+1) { throw 'Archive file count differs from package.' }
        $prefix = [IO.Path]::GetFileName($root)+'/'
        foreach ($entry in $entries) {
            if (-not $entry.FullName.StartsWith($prefix,[StringComparison]::Ordinal)) { throw 'Unexpected archive path.' }
            $relative = $entry.FullName.Substring($prefix.Length)
            if ($relative -ne 'SHA256SUMS.txt' -and -not $listed.ContainsKey($relative)) { throw 'Unexpected archive file.' }
            $stream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try {
                $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')
                if ($actual -ne (Get-FileHash -LiteralPath (Join-Path $root $relative)).Hash) { throw ('Archive contents differ: '+$relative) }
            } finally { $sha.Dispose(); $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
}
Write-Output ('PASS '+$manifest.Version+' '+$manifest.Mode+': '+$listed.Count+' file checksums, runtime and ZIP contents')
