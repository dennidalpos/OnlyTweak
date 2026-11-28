#requires -Version 5.1
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$result=@{State='Inconclusive';Summary='Diagnostica incompleta.';Evidence=@();AuthenticationVerified=$false;ErrorCode='';Data=$null}
try {
    $line=[Console]::In.ReadLine()
    if (!$line -or $line.Length -gt 16384) { throw 'InvalidRequest' }
    $request=$line | ConvertFrom-Json
    if ($request.ActionId -notin @('local.dns','local.tcp')) { throw 'InvalidAction' }
    $target=[string]$request.Target
    $address=$null
    if ([Net.IPAddress]::TryParse($target,[ref]$address)) { throw 'InvalidTarget' }
    if ($target.Length -gt 253 -or !$target.Contains('.') -or @($target.Split('.') | Where-Object {$_ -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$'}).Count) { throw 'InvalidTarget' }
    if ($request.ActionId -eq 'local.dns') {
        $type=[string]$request.Parameters.recordType
        if ($type -notin @('A','AAAA','CNAME')) { throw 'InvalidRecordType' }
        if (!(Get-Command 'Resolve-DnsName' -CommandType Cmdlet -ErrorAction SilentlyContinue)) { throw 'MissingCmdlet' }
        $records=@(Resolve-DnsName -Name $target -Type $type -DnsOnly -ErrorAction Stop | Select-Object Name,Type,IPAddress,NameHost,TTL)
        $result.Data=@{Target=$target;RecordType=$type;ReadComplete=$true;Records=$records}
        $result.State=if($records.Count){'Success'}else{'ObservedIssue'}
        $result.Summary=if($records.Count){'Query DNS completata: leggere i record restituiti.'}else{'Query completata senza record restituiti.'}
    } else {
        $port=0
        if (![int]::TryParse([string]$request.Parameters.port,[ref]$port) -or $port -lt 1 -or $port -gt 65535) { throw 'InvalidPort' }
        if (!(Get-Command 'Test-NetConnection' -CommandType Function,Cmdlet -ErrorAction SilentlyContinue)) { throw 'MissingCmdlet' }
        $test=Test-NetConnection -ComputerName $target -Port $port -InformationLevel Detailed -WarningAction SilentlyContinue -ErrorAction Stop
        if ($null -eq $test -or $test.TcpTestSucceeded -isnot [bool]) { throw 'IncompleteResult' }
        $result.Data=@{Target=$target;Port=$port;ReadComplete=$true;TcpTestSucceeded=$test.TcpTestSucceeded;RemoteAddress=[string]$test.RemoteAddress;SourceAddress=[string]$test.SourceAddress}
        $result.State=if($test.TcpTestSucceeded){'Success'}else{'ObservedIssue'}
        $result.Summary=if($test.TcpTestSucceeded){'Connessione TCP riuscita verso la porta selezionata.'}else{'Connessione TCP non riuscita: non dimostra da sola un guasto generale.'}
    }
} catch {
    $result.State=if($_.Exception.Message -eq 'MissingCmdlet'){'Unavailable'}else{'Inconclusive'}
    $result.ErrorCode=if($_.Exception.Message -in @('MissingCmdlet','InvalidRequest','InvalidAction','InvalidTarget','InvalidPort','InvalidRecordType','IncompleteResult')){$_.Exception.Message}else{'DiagnosticReadFailed'}
    $result.Summary='Diagnostica non completata: verificare disponibilità, policy e destinazione.'
}
$result | ConvertTo-Json -Depth 12 -Compress
