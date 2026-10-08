param(
    [Parameter(Mandatory)][string[]]$Executables,
    [string]$Output = 'artifacts\idle-comparison.json',
    [ValidateRange(1,3600)][int]$WarmupSeconds = 8,
    [ValidateRange(1,3600)][int]$SampleSeconds = 8,
    [ValidateRange(1,100)][int]$Rounds = 1,
    [switch]$KeepLastRunning
)
$ErrorActionPreference = 'Stop'
$results = @()
$iteration = 0
foreach ($round in 1..$Rounds) {
    foreach ($executable in $Executables) {
        $iteration++
        $path = (Resolve-Path -LiteralPath $executable).Path
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $app = Start-Process -FilePath $path -ArgumentList '--background' -WindowStyle Hidden -PassThru
        try {
            $ready = $app.WaitForInputIdle(10000)
            $readyMs = $clock.ElapsedMilliseconds
            Start-Sleep -Seconds $WarmupSeconds
            $app.Refresh()
            if ($app.HasExited) { throw "App exited before sampling: $path" }
            $cpuStart = $app.TotalProcessorTime.TotalSeconds
            $clock.Restart()
            $workingSet = @()
            $privateBytes = @()
            for ($i = 0; $i -lt $SampleSeconds; $i++) {
                Start-Sleep -Seconds 1
                $app.Refresh()
                $workingSet += $app.WorkingSet64 / 1MB
                $privateBytes += $app.PrivateMemorySize64 / 1MB
            }
            $result = [pscustomobject]@{
                Executable = $path; ProcessId = $app.Id; Round = $round; InputIdleReached = $ready; InputIdleMs = $readyMs
                SampleSeconds = [math]::Round($clock.Elapsed.TotalSeconds, 2)
                CpuPercent = [math]::Round(($app.TotalProcessorTime.TotalSeconds - $cpuStart) / $clock.Elapsed.TotalSeconds / [Environment]::ProcessorCount * 100, 3)
                WorkingSetMiB = [math]::Round(($workingSet | Measure-Object -Average).Average, 1)
                PeakSampleWorkingSetMiB = [math]::Round(($workingSet | Measure-Object -Maximum).Maximum, 1)
                PrivateBytesMiB = [math]::Round(($privateBytes | Measure-Object -Average).Average, 1)
            }
            $results += $result
            $result | Format-List
        } finally {
            if (-not $app.HasExited -and -not ($KeepLastRunning -and $iteration -eq $Rounds * $Executables.Count)) { Stop-Process -Id $app.Id }
            $app.Dispose()
        }
    }
}
$results | ConvertTo-Json | Set-Content -LiteralPath $Output
