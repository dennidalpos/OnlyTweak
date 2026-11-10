[CmdletBinding()]
param(
    [string]$CertificateThumbprint,
    [string]$TimestampServer,
    [string]$SignToolPath,
    [switch]$RequireSigned
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'ReleaseEvidence.psm1') -Force
$build=Get-OnlyTweakPublishedBuild $root
$exe=$build.Exe
# Check the actual embedded bytes before signing or changing any delivery artifact.
$scriptSignatures=@(foreach ($script in $build.Evidence.Scripts) {
    $bytes=[Convert]::FromBase64String($script.Content)
    $status=Get-AuthenticodeSignature -Content $bytes -SourcePathOrExtension ([IO.Path]::GetExtension($script.Name))
    if ($RequireSigned -and $status.Status -ne 'Valid') { throw ('Firma valida richiesta per la risorsa incorporata: '+$script.Name) }
    [pscustomobject]@{Name=$script.Name;Sha256=$script.Sha256;AuthenticodeStatus="$($status.Status)"}
})
if ($CertificateThumbprint) {
    if ($CertificateThumbprint -notmatch '^[A-Fa-f0-9]{40}$' -or -not $TimestampServer -or
        ([uri]$TimestampServer).Scheme -ne 'https' -or -not [IO.Path]::IsPathRooted($SignToolPath) -or
        -not (Test-Path -LiteralPath $SignToolPath)) { throw 'Indicare thumbprint, timestamp HTTPS e percorso completo di signtool.exe.' }
    # Authenticode changes only the local artifact. No publishing or upload.
    & $SignToolPath sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampServer /td SHA256 $exe
    if ($LASTEXITCODE -ne 0) { throw 'Firma Authenticode fallita.' }
    $signedInspection=Invoke-OnlyTweakInspection $exe
    if ($signedInspection.Sha256 -cne $build.EvidenceSha256) { throw 'Risorse della build cambiate durante la firma.' }
    [ordered]@{SchemaVersion=1;ExeSha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash;EvidenceSha256=$build.EvidenceSha256} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'artifacts/build-evidence/publish.json') -Encoding utf8
}
$signature=Get-AuthenticodeSignature -LiteralPath $exe
if ($RequireSigned -and $signature.Status -ne 'Valid') { throw 'Firma valida obbligatoria per la consegna aziendale.' }
$build=Get-OnlyTweakPublishedBuild $root
$folder=Join-Path $root 'artifacts\delivery\2.0.0'
[void][IO.Directory]::CreateDirectory($folder)
$manifest=[ordered]@{
    schemaVersion=2; product='OnlyTweakWindows'; version=$build.Evidence.Metadata.Version; createdAt=[DateTimeOffset]::UtcNow.ToString('o')
    sha256=$build.ExeSha256; length=(Get-Item -LiteralPath $exe).Length
    authenticodeStatus="$($signature.Status)"; runtimeTarget='net10.0-windows/win-x64'; selfContained=$true
    runtimePacks=@($build.Evidence.Metadata.RuntimePacks); dependencies=@($build.Evidence.Metadata.Dependencies)
    embeddedScripts=$scriptSignatures; buildEvidenceSha256=$build.EvidenceSha256; buildEvidence=$build.Evidence.Metadata
    qualification='Local build/tests only. Domain lab, OS matrix, signing and customer pilot tracked separately.'
}
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $folder 'manifest.json') -Encoding utf8
Copy-Item -LiteralPath $exe -Destination (Join-Path $folder 'OnlyTweakWindows.exe') -Force
if ((Get-FileHash -LiteralPath (Join-Path $folder 'OnlyTweakWindows.exe') -Algorithm SHA256).Hash -cne $build.ExeSha256) { throw 'Copia EXE non coerente con il manifest.' }
Write-Output ('Consegna locale: '+$folder+'; firma '+$signature.Status)
