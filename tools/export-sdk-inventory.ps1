param(
    [Parameter(Mandatory=$true)][ValidateSet('WeChat','Douyin','TikTok','BGDT')][string]$Platform,
    [Parameter(Mandatory=$true)][string]$PackageRoot,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $PackageRoot).Path
$destination=[IO.Path]::GetFullPath($OutputPath)
if($destination.StartsWith($root.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Write inventory outside the SDK package to avoid including the report in its own snapshot'
}
$manifest=Join-Path $root 'package.json'
$metadata=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if(!$metadata.name -or !$metadata.version){throw 'Package name and exact version are required'}
$files=@(Get-ChildItem -LiteralPath $root -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
    [ordered]@{
        path=$_.FullName.Substring($root.Length).TrimStart('\','/').Replace('\','/')
        bytes=$_.Length
        sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
$inventory=[ordered]@{
    schemaVersion=2
    coverage='all-files'
    platform=$Platform
    packageName=$metadata.name
    packageVersion=$metadata.version
    source='local-installed-package'
    authenticityVerified=$false
    capturedAt=[DateTimeOffset]::UtcNow.ToString('o')
    files=$files
}
$inventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output "Inventory written: $Platform $($metadata.version), $($files.Count) files. This is not an authenticity or compatibility certificate."
