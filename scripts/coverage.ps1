<#
.SYNOPSIS
  Runs all tests with coverage, writes an HTML + Markdown report, and fails if the gated
  assemblies are below the line-coverage threshold (ROADMAP D2.7; DESIGN.md §15).

.EXAMPLE
  pwsh scripts/coverage.ps1              # Windows PowerShell 5.1 works too
  pwsh scripts/coverage.ps1 -NoBuild     # after a successful `dotnet build`
#>
param(
    [double] $Threshold = 85,
    [string[]] $Gated = @('DocViewer.Domain', 'DocViewer.Application'),
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$results = Join-Path $root 'TestResults/coverage'
$report = Join-Path $results 'report'

if (Test-Path $results) { Remove-Item -Recurse -Force $results }

$testArgs = @('test', $root, '--collect:XPlat Code Coverage', '--results-directory', $results)
if ($NoBuild) { $testArgs += '--no-build' }
& dotnet @testArgs
if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE); coverage not evaluated." }

& dotnet tool run reportgenerator `
    "-reports:$results/**/coverage.cobertura.xml" `
    "-targetdir:$report" `
    '-reporttypes:Html;JsonSummary;MarkdownSummaryGithub' `
    '-assemblyfilters:+DocViewer.*' `
    '-filefilters:-*/Migrations/*;-*/obj/*;-*.g.cs;-*.generated.cs'
if ($LASTEXITCODE -ne 0) { throw "ReportGenerator failed (exit $LASTEXITCODE)." }

$summary = Get-Content (Join-Path $report 'Summary.json') -Raw | ConvertFrom-Json
$failed = $false
Write-Host ''
Write-Host ('{0,-32} {1,8}' -f 'Assembly', 'Line %')
foreach ($assembly in $summary.coverage.assemblies | Sort-Object name) {
    $gate = $Gated -contains $assembly.name
    $mark = ''
    if ($gate -and $assembly.coverage -lt $Threshold) { $mark = "  < $Threshold (FAIL)"; $failed = $true }
    elseif ($gate) { $mark = "  >= $Threshold" }
    Write-Host ('{0,-32} {1,8:N1}{2}' -f $assembly.name, $assembly.coverage, $mark)
}
Write-Host ''
Write-Host "HTML report: $report/index.html"

if ($failed) { throw "Coverage gate failed: gated assemblies must have at least $Threshold % line coverage." }
