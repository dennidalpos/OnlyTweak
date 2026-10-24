[CmdletBinding()]
param(
    [string]$CertificateThumbprint,
    [string]$TimestampServer,
    [string]$SignToolPath,
    [switch]$RequireSigned
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$exe=Join-Path $root 'dist\OnlyTweakWindows.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Eseguire build.cmd prima di preparare la consegna.' }
if ($CertificateThumbprint) {
    if ($CertificateThumbprint -notmatch '^[A-Fa-f0-9]{40}$' -or -not $TimestampServer -or
        ([uri]$TimestampServer).Scheme -ne 'https' -or -not [IO.Path]::IsPathRooted($SignToolPath) -or
        -not (Test-Path -LiteralPath $SignToolPath)) { throw 'Indicare thumbprint, timestamp HTTPS e percorso completo di signtool.exe.' }
    # Authenticode changes only the local artifact. No publishing or upload.
    & $SignToolPath sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampServer /td SHA256 $exe
    if ($LASTEXITCODE -ne 0) { throw 'Firma Authenticode fallita.' }
}
$signature=Get-AuthenticodeSignature -LiteralPath $exe
if ($RequireSigned -and $signature.Status -ne 'Valid') { throw 'Firma valida obbligatoria per la consegna aziendale.' }
if ($RequireSigned) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'scripts\enterprise') -File |
        Where-Object Extension -in '.ps1','.psm1') {
        if ((Get-AuthenticodeSignature -LiteralPath $file.FullName).Status -ne 'Valid') {
            throw ('Firma valida richiesta per lo script incorporato: '+$file.Name)
        }
    }
}
$folder=Join-Path $root 'artifacts\delivery\2.0.0'
[void][IO.Directory]::CreateDirectory($folder)
$assets=Get-Content -LiteralPath (Join-Path $root 'obj\project.assets.json') -Raw | ConvertFrom-Json
$libraries=@($assets.libraries.PSObject.Properties | ForEach-Object { [pscustomobject]@{Name=$_.Name;Type=$_.Value.type;Sha512=$_.Value.sha512} })
$manifest=[ordered]@{
    schemaVersion=1; product='OnlyTweakWindows'; version='2.0.0'; createdAt=[DateTimeOffset]::UtcNow.ToString('o')
    sha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash; length=(Get-Item -LiteralPath $exe).Length
    authenticodeStatus="$($signature.Status)"; runtimeTarget='net10.0-windows/win-x64'; selfContained=$true
    runtimePacks=@($assets.project.frameworks.PSObject.Properties.Value.downloadDependencies | Select-Object name,version)
    dependencies=$libraries
    qualification='Local build/tests only. Domain lab, OS matrix, signing and customer pilot tracked separately.'
}
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $folder 'manifest.json') -Encoding utf8
Copy-Item -LiteralPath $exe -Destination (Join-Path $folder 'OnlyTweakWindows.exe') -Force
Write-Output ('Consegna locale: '+$folder+'; firma '+$signature.Status)
