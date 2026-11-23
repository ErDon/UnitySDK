param(
    [Parameter(Mandatory=$true)][ValidateSet('WeChat','Douyin','TikTok','BGDT')][string]$Platform,
    [Parameter(Mandatory=$true)][string]$PackageRoot,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $PackageRoot).Path
$manifest=Join-Path $root 'package.json'
$metadata=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if(!$metadata.name -or !$metadata.version){throw 'Package name and exact version are required'}
$files=@(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
    $_.Extension -in @('.dll','.asmdef','.jslib','.meta','.cs') -or $_.FullName -eq $manifest
} | Sort-Object FullName | ForEach-Object {
    [ordered]@{
        path=$_.FullName.Substring($root.Length).TrimStart('\','/').Replace('\','/')
        bytes=$_.Length
        sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
$inventory=[ordered]@{
    schemaVersion=1
    platform=$Platform
    packageName=$metadata.name
    packageVersion=$metadata.version
    source='local-installed-package'
    authenticityVerified=$false
    capturedAt=[DateTimeOffset]::UtcNow.ToString('o')
    files=$files
}
$inventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output "Inventory written: $Platform $($metadata.version), $($files.Count) selected files. This is not an authenticity or compatibility certificate."
