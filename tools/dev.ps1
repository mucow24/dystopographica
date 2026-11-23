<#
.SYNOPSIS
  Development helper: runs headless Unity against the ModKit project in ./modkit.

.EXAMPLE
  pwsh tools/dev.ps1 test
  Compiles the package and runs the edit-mode tests.

.EXAMPLE
  pwsh tools/dev.ps1 run Dystopographica.Cli.Info
  Runs one static method with -executeMethod. Extra arguments are passed to Unity.

.NOTES
  Unity allows one instance per project, so close the Unity editor first.
#>
param(
    [Parameter(Mandatory, Position = 0)][ValidateSet("test", "run")][string]$Command,
    [Parameter(Position = 1)][string]$Method,
    [Parameter(ValueFromRemainingArguments)][string[]]$UnityArgs
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "modkit"
$logs = Join-Path $project "Logs"

function Get-UnityPath {
    $versionFile = Join-Path $project "ProjectSettings/ProjectVersion.txt"
    if (-not (Test-Path $versionFile)) {
        throw "No Unity project at $project. Unzip the ModKit template there first (see CLAUDE.md)."
    }
    $version = (Select-String -Path $versionFile -Pattern "^m_EditorVersion: (.+)$").Matches[0].Groups[1].Value.Trim()
    $candidates = @(
        $env:UNITY_EDITOR,
        "C:\Program Files\Unity $version\Editor\Unity.exe",
        "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }
    if (-not $candidates) { throw "Unity $version not found. Install it, or set UNITY_EDITOR to its Unity.exe." }
    return @($candidates)[0]
}

# Runs Unity in batch mode, waits for it, and returns its exit code.
function Invoke-Unity([string]$Name, [string[]]$Arguments) {
    New-Item -ItemType Directory -Force $logs | Out-Null
    $script:log = Join-Path $logs "dev-$Name.log"
    Remove-Item $script:log -ErrorAction SilentlyContinue
    $all = @("-batchmode", "-projectPath", $project, "-logFile", $script:log) + $Arguments
    $quoted = $all | ForEach-Object { if ($_ -match "\s") { "`"$_`"" } else { $_ } }
    # Not Start-Process -Wait: that also waits for the licensing helper Unity leaves
    # running, which adds ten minutes. Reading Handle first keeps ExitCode available.
    $process = Start-Process -FilePath (Get-UnityPath) -ArgumentList ($quoted -join " ") -PassThru
    $null = $process.Handle
    $process.WaitForExit()
    return $process.ExitCode
}

# Prints the lines of the Unity log that matter: compiler errors, our own output, and known blockers.
function Show-LogHighlights {
    if (-not (Test-Path $script:log)) { Write-Host "No Unity log was written."; return }
    $text = Get-Content $script:log
    if ($text -match "Multiple Unity instances cannot open the same project|another Unity instance is running") {
        Write-Host "The Unity editor has this project open. Close it and run again." -ForegroundColor Yellow
    }
    $text | Where-Object { $_ -match "error CS\d+|\[Dystopographica\]" } | Select-Object -Unique | ForEach-Object { Write-Host $_ }
}

switch ($Command) {
    "test" {
        $results = Join-Path $logs "test-results.xml"
        Remove-Item $results -ErrorAction SilentlyContinue
        # Only our assembly: the ModKit project also carries a sample test from Addressables.
        $code = Invoke-Unity "test" @("-runTests", "-testPlatform", "EditMode", "-assemblyNames", "Dystopographica.Editor.Tests", "-testResults", $results)
        Show-LogHighlights
        if (-not (Test-Path $results)) {
            Write-Host "FAILED: Unity wrote no test results (exit code $code). See $script:log" -ForegroundColor Red
            exit 1
        }
        $run = ([xml](Get-Content $results -Raw)).'test-run'
        foreach ($case in $run.SelectNodes("//test-case[@result='Failed']")) {
            Write-Host "FAIL $($case.fullname)" -ForegroundColor Red
            Write-Host "     $($case.failure.message.InnerText.Trim())"
        }
        $summary = "$($run.passed) passed, $($run.failed) failed, $($run.skipped) skipped, $($run.total) total"
        if ([int]$run.failed -gt 0 -or [int]$run.total -eq 0) { Write-Host "FAILED: $summary" -ForegroundColor Red; exit 1 }
        Write-Host "OK: $summary" -ForegroundColor Green
    }
    "run" {
        if (-not $Method) { throw "Usage: dev.ps1 run <Namespace.Class.Method> [unity args]" }
        $code = Invoke-Unity "run" (@("-quit", "-executeMethod", $Method) + $UnityArgs)
        Show-LogHighlights
        if ($code -ne 0) { Write-Host "FAILED: Unity exited with code $code. See $script:log" -ForegroundColor Red; exit 1 }
        Write-Host "OK" -ForegroundColor Green
    }
}
