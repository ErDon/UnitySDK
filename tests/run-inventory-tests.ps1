$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $dir='.tests/inventory';New-Item -ItemType Directory -Force $dir | Out-Null
    function Save-Fixture($name,$files,$platform='WeChat',$version='1.0.0') {
        @{schemaVersion=1;platform=$platform;packageName='test.sdk';packageVersion=$version;files=@($files)} | ConvertTo-Json -Depth 5 | Set-Content "$dir/$name.json"
    }
    function File-Record($path,$hash='a') {return @{path=$path;bytes=1;sha256=($hash*64)}}
    Save-Fixture before @((File-Record 'sdk.dll'),(File-Record 'old.cs'))
    Save-Fixture after @((File-Record 'sdk.dll' 'b'),(File-Record 'new.meta')) 'WeChat' '1.1.0'
    & ./tools/compare-sdk-inventory.ps1 "$dir/before.json" "$dir/after.json" "$dir/report.json"
    $r=Get-Content "$dir/report.json" -Raw | ConvertFrom-Json
    if($r.changedFileCount -ne 3 -or !$r.versionChanged -or $r.compatibility -ne 'not-assessed'){throw 'Change report incorrect'}
    foreach($kind in @('added','removed','modified')) {if(!($r.changes | Where-Object kind -eq $kind)){throw 'Missing change kind'}}
    Save-Fixture sameversion @((File-Record 'sdk.dll' 'c'),(File-Record 'old.cs'))
    & ./tools/compare-sdk-inventory.ps1 "$dir/before.json" "$dir/sameversion.json" "$dir/report.json"
    $r=Get-Content "$dir/report.json" -Raw | ConvertFrom-Json
    if($r.changedFileCount -ne 1 -or $r.versionChanged){throw 'Same-version content changes missed'}
    & ./tools/compare-sdk-inventory.ps1 "$dir/before.json" "$dir/before.json" "$dir/report.json"
    $r=Get-Content "$dir/report.json" -Raw | ConvertFrom-Json
    if($r.changedFileCount -ne 0 -or $r.versionChanged){throw 'Identical snapshot mismatch'}
    foreach($bad in @(@((File-Record '../escape.dll')),@((File-Record 'same.cs'),(File-Record 'SAME.cs')),@(@{path='x.dll';bytes=1;sha256='bad'}))) {
        Save-Fixture invalid $bad
        $rejected=$false;try{& ./tools/compare-sdk-inventory.ps1 "$dir/before.json" "$dir/invalid.json" "$dir/report.json"}catch{$rejected=$true}
        if(!$rejected){throw 'Invalid input accepted'}
    }
    Save-Fixture invalid @() 'TikTok'
    $rejected=$false;try{& ./tools/compare-sdk-inventory.ps1 "$dir/before.json" "$dir/invalid.json" "$dir/report.json"}catch{$rejected=$true}
    if(!$rejected){throw 'Cross-platform comparison accepted'}
    $rejected=$false;try{& ./tools/compare-sdk-inventory.ps1 "$dir/before.json" "$dir/before.json" "$dir/before.json"}catch{$rejected=$true}
    if(!$rejected){throw 'Input overwrite accepted'}
    Write-Output 'PASS: inventory changes, unchanged baseline, invalid paths/hashes/duplicates, platform identity and input preservation'
} finally {Pop-Location}
