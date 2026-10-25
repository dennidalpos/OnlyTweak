#requires -Version 7.4
[CmdletBinding()]
param(
    [ValidateSet('Embed','Receipt')][string]$Mode='Embed',
    [string]$AssetsPath,
    [string]$OutputPath,
    [string]$RuntimePackList,
    [string]$TargetFramework,
    [string]$RuntimeIdentifier,
    [string]$SelfContained
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'ReleaseEvidence.psm1') -Force
if ($Mode -eq 'Receipt') {
    $exe=Join-Path $root 'dist/OnlyTweakWindows.exe'
    $hash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    $inspection=Invoke-OnlyTweakInspection $exe
    Assert-OnlyTweakBuild $root $inspection.Value
    if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -cne $hash) { throw 'EXE cambiato durante ispezione.' }
    $output=Join-Path $root 'artifacts/build-evidence/publish.json'
    [void][IO.Directory]::CreateDirectory((Split-Path $output))
    [ordered]@{SchemaVersion=1;ExeSha256=$hash;EvidenceSha256=$inspection.Sha256} | ConvertTo-Json |
        Set-Content -LiteralPath $output -Encoding utf8
    'Provenienza del bundle verificata e registrata.'
    return
}
if ($TargetFramework -ne 'net10.0-windows' -or $RuntimeIdentifier -ne 'win-x64' -or $SelfContained -ne 'true') {
    throw 'Provenienza supportata solo per la build autonoma net10.0-windows/win-x64.'
}
$assets=Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json -Depth 64
$libraries=@($assets.libraries.PSObject.Properties | Sort-Object Name | ForEach-Object {
    [pscustomobject]@{Name=$_.Name;Type=$_.Value.type;Sha512=$_.Value.sha512}
})
$packs=@($RuntimePackList.Split(';',[StringSplitOptions]::RemoveEmptyEntries) | Sort-Object -Unique | ForEach-Object {
    $parts=$_.Split('@'); if ($parts.Count -ne 2 -or -not $parts[0] -or -not $parts[1]) { throw 'Runtime pack non risolto.' }
    [pscustomobject]@{Name=$parts[0];Version=$parts[1]}
})
if ($packs.Count -lt 2) { throw 'Runtime pack della pubblicazione autonoma non risolti.' }
$metadata=[ordered]@{
    SchemaVersion=1;Product='OnlyTweakWindows';Version='2.0.0';RuntimeTarget='net10.0-windows/win-x64';SelfContained=$true
    SourceFiles=@(Get-OnlyTweakSourceFiles $root);AssetsSha256=(Get-FileHash -LiteralPath $AssetsPath -Algorithm SHA256).Hash
    RuntimePacks=$packs;Dependencies=$libraries
}
$json=$metadata | ConvertTo-Json -Depth 12
[void][IO.Directory]::CreateDirectory((Split-Path $OutputPath))
if (-not (Test-Path -LiteralPath $OutputPath) -or [IO.File]::ReadAllText($OutputPath) -cne $json) {
    [IO.File]::WriteAllText($OutputPath,$json,[Text.UTF8Encoding]::new($false))
}
