param(
    [Parameter(Mandatory=$true)][string]$BeforePath,
    [Parameter(Mandatory=$true)][string]$AfterPath,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference='Stop'
function Read-Inventory([string]$path) {
    $data=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if($data.schemaVersion -notin @(1,2) -or !$data.platform -or !$data.packageName -or !$data.packageVersion -or $null -eq $data.files){throw 'Invalid SDK inventory header'}
    if($data.schemaVersion -eq 2 -and $data.coverage -ne 'all-files'){throw 'Unsupported inventory coverage'}
    $index=@{}
    foreach($file in $data.files) {
        $name=[string]$file.path
        if(!$name -or $name.Contains('\') -or $name.StartsWith('/') -or $name.Contains(':') -or ($name.Split('/') | Where-Object {$_ -in @('','..','.')})){throw 'Inventory must contain normalized relative paths'}
        if($index.ContainsKey($name)){throw 'Duplicate or case-colliding inventory path'}
        if($file.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or $null -eq $file.bytes -or $file.bytes -lt 0){throw 'Invalid file hash or byte count'}
        $index[$name]=$file
    }
    return @{Data=$data;Index=$index}
}
$before=Read-Inventory $BeforePath
$after=Read-Inventory $AfterPath
if($before.Data.platform -cne $after.Data.platform -or $before.Data.packageName -cne $after.Data.packageName){throw 'Cannot compare different platform or package identities'}
if($before.Data.schemaVersion -ne $after.Data.schemaVersion){throw 'Inventory coverage differs; recapture both versions with the same exporter'}
function Get-ReviewArea([string]$name) {
    switch ([IO.Path]::GetExtension($name).ToLowerInvariant()) {
        '.dll' {return 'binary-api-and-runtime'}
        '.cs' {return 'source-api-and-behavior'}
        '.meta' {return 'import-settings-and-guid'}
        '.asmdef' {return 'assembly-and-platform-isolation'}
        '.jslib' {return 'javascript-bridge'}
        '.js' {return 'javascript-runtime'}
        '.wasm' {return 'wasm-runtime'}
        '.json' {return 'package-or-runtime-configuration'}
        default {return 'resources-and-toolchain'}
    }
}
$changes=@(foreach($key in @(@($before.Index.Keys)+@($after.Index.Keys) | Sort-Object -Unique)) {
    $old=$before.Index[$key];$new=$after.Index[$key]
    $kind=if($null -eq $old){'added'}elseif($null -eq $new){'removed'}elseif($old.sha256 -ine $new.sha256 -or $old.bytes -ne $new.bytes -or $old.path -cne $new.path){'modified'}else{''}
    if($kind){[ordered]@{path=$key;kind=$kind;reviewArea=Get-ReviewArea $key;before=$old;after=$new}}
})
$report=[ordered]@{
    schemaVersion=1;platform=$after.Data.platform;packageName=$after.Data.packageName
    beforeVersion=$before.Data.packageVersion;afterVersion=$after.Data.packageVersion
    versionChanged=($before.Data.packageVersion -cne $after.Data.packageVersion)
    changedFileCount=$changes.Count;changes=$changes
    compatibility='not-assessed';authenticityVerified=$false
    coverage=$(if($after.Data.schemaVersion -eq 2){'all-files'}else{'legacy-selected-files'})
    limitation='File hashes do not establish platform compatibility or official authenticity.'
}
$destination=[IO.Path]::GetFullPath($OutputPath)
if($destination -ieq (Resolve-Path -LiteralPath $BeforePath).Path -or $destination -ieq (Resolve-Path -LiteralPath $AfterPath).Path){throw 'Report must not overwrite an input inventory'}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output "SDK comparison written: $($changes.Count) file changes; compatibility is not assessed."
