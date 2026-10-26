# PowerShell 7.4+: Authenticode -Content preserves the embedded UTF-8 script bytes.
Set-StrictMode -Version 3
function Get-OnlyTweakSourceFiles([string]$Root) {
    $files=@(Get-ChildItem -LiteralPath (Join-Path $Root 'src') -Filter '*.cs' -File -Recurse)
    $files+=@(Get-ChildItem -LiteralPath (Join-Path $Root 'scripts\enterprise') -File | Where-Object Extension -in '.ps1','.psm1')
    foreach ($name in @('OnlyTweakWindows.csproj','app.manifest','packages.lock.json','global.json','assets/OnlyTweak.ico',
        'build.cmd','scripts/write-build-evidence.ps1','scripts/ReleaseEvidence.psm1','scripts/prepare-release.ps1')) {
        $files+=Get-Item -LiteralPath (Join-Path $Root $name)
    }
    @($files | ForEach-Object {
        [pscustomobject]@{Name=[IO.Path]::GetRelativePath($Root,$_.FullName).Replace('\','/');Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    } | Sort-Object Name)
}
function Invoke-OnlyTweakInspection([string]$Exe) {
    $start=[Diagnostics.ProcessStartInfo]::new($Exe)
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $start.StandardOutputEncoding=[Text.UTF8Encoding]::new($false)
    $start.ArgumentList.Add('--release-inspect')
    $process=[Diagnostics.Process]::new(); $process.StartInfo=$start
    try {
        if (-not $process.Start()) { throw 'Avvio ispezione build fallito.' }
        $output=$process.StandardOutput.ReadToEndAsync(); $errorOutput=$process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { $process.Kill($true); $process.WaitForExit(); throw 'Timeout ispezione build.' }
        $json=$output.GetAwaiter().GetResult(); $null=$errorOutput.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0 -or $json.Length -gt 1048576) { throw 'Ispezione build non valida.' }
        $value=$json | ConvertFrom-Json -Depth 32
        if ($value.SchemaVersion -ne 1 -or $value.Metadata.SchemaVersion -ne 1) { throw 'Schema provenienza non supportato.' }
        [pscustomobject]@{Value=$value;Sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($json)))}
    } finally { $process.Dispose() }
}
function Assert-OnlyTweakBuild([string]$Root,$Evidence) {
    $metadata=$Evidence.Metadata
    if ($metadata.Product -ne 'OnlyTweakWindows' -or $metadata.RuntimeTarget -ne 'net10.0-windows/win-x64' -or -not $metadata.SelfContained -or
        @($metadata.RuntimePacks).Count -lt 2) { throw 'Identita/runtime della build non validi.' }
    $current=@(Get-OnlyTweakSourceFiles $Root)
    if (@($metadata.SourceFiles).Count -ne $current.Count) { throw 'Sorgenti diversi dalla build: ricompilare.' }
    foreach ($file in $current) {
        $bound=@($metadata.SourceFiles | Where-Object Name -CEQ $file.Name)
        if ($bound.Count -ne 1 -or $bound[0].Sha256 -cne $file.Sha256) { throw ('Sorgente diverso dalla build: '+$file.Name) }
    }
    if ((Get-FileHash -LiteralPath (Join-Path $Root 'obj/project.assets.json') -Algorithm SHA256).Hash -cne $metadata.AssetsSha256) {
        throw 'Assets diversi dalla build: ricompilare.'
    }
    $expected=@('Operations.ps1','Provision-Remoting.ps1','OnlyTweakEndpoint.psm1','LocalDiagnostics.ps1')
    if (@($Evidence.Scripts).Count -ne $expected.Count) { throw 'Risorse script incomplete.' }
    foreach ($name in $expected) {
        $script=@($Evidence.Scripts | Where-Object Name -CEQ $name)
        if ($script.Count -ne 1) { throw 'Risorsa script assente/duplicata.' }
        $bytes=[Convert]::FromBase64String($script[0].Content)
        $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
        $source=@($current | Where-Object Name -CEQ ('scripts/enterprise/'+$name))
        if ($hash -cne $script[0].Sha256 -or $hash -cne $source[0].Sha256) { throw ('Risorsa incorporata diversa dal sorgente: '+$name) }
    }
}
function Get-OnlyTweakPublishedBuild([string]$Root) {
    $exe=Join-Path $Root 'dist/OnlyTweakWindows.exe'
    $receiptPath=Join-Path $Root 'artifacts/build-evidence/publish.json'
    if (-not (Test-Path -LiteralPath $receiptPath)) { throw 'Ricevuta build assente: eseguire build.cmd.' }
    $receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json -Depth 32
    $hash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    if ($receipt.SchemaVersion -ne 1 -or $receipt.ExeSha256 -cne $hash) { throw 'EXE diverso dalla build registrata: ricompilare.' }
    $inspection=Invoke-OnlyTweakInspection $exe
    if ($inspection.Sha256 -cne $receipt.EvidenceSha256 -or (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -cne $hash) { throw 'Provenienza EXE non coerente.' }
    Assert-OnlyTweakBuild $Root $inspection.Value
    [pscustomobject]@{Exe=$exe;ExeSha256=$hash;EvidenceSha256=$inspection.Sha256;Evidence=$inspection.Value}
}
Export-ModuleMember -Function Get-OnlyTweakSourceFiles,Invoke-OnlyTweakInspection,Assert-OnlyTweakBuild,Get-OnlyTweakPublishedBuild
