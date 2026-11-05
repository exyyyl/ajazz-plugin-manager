param([Parameter(Mandatory=$true)][string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
# An explicit directory keeps binaries and evidence outside the release payload.
# Run on Windows with UAC available. Uses only test TCP 27936 and separate GUIDs.
$output = (Resolve-Path -LiteralPath $ReportDirectory).Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExe = Join-Path $output 'ObsGuardTests.exe'
& $compiler /nologo /platform:x64 /define:OBS_GUARD_TEST /r:System.Security.dll "/out:$testExe" (Join-Path $PSScriptRoot '..\src\ObsGuard.cs') (Join-Path $PSScriptRoot 'ObsGuardTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Guard test compilation failed' }
$report = Join-Path $output ('wfp-' + [Guid]::NewGuid().ToString('N') + '.txt')
try {
    $test = Start-Process -FilePath $testExe -ArgumentList @('--keep',('"' + $report + '"')) -Verb RunAs -WindowStyle Hidden -PassThru -Wait
    Get-Content -LiteralPath $report
    if ($test.ExitCode -ne 0) { throw 'Elevated guard checks failed' }
    & $testExe --check
    if ($LASTEXITCODE -ne 0) { throw 'Normal-user policy verification failed' }
} finally {
    $cleanup = Start-Process -FilePath $testExe -ArgumentList '--cleanup' -Verb RunAs -WindowStyle Hidden -PassThru -Wait
    if ($cleanup.ExitCode -ne 0) { throw 'Temporary test policy cleanup failed' }
}
