<#
    export-for-sql2019.ps1

    Writes the Avijan database out as a plain .sql script that runs on SQL Server
    2019 (and anything newer).

    Why not a .bak: a SQL Server backup only restores forward. This server runs
    SQL Server 2025, which stamps the database at internal version 998; SQL Server
    2019 reads up to 904 and refuses the file outright. Nothing can be done to the
    .bak to change that, so the data has to leave as script rather than as pages.

    SMO's TargetServerVersion is what makes the output safe: it emits syntax the
    named version understands rather than whatever this server happens to support.
#>

param(
    [string] $Server   = '.\SQLEXPRESS',
    [string] $Database = 'Avijan',
    [Parameter(Mandatory = $true)] [string] $Out
)

$ErrorActionPreference = 'Stop'
Import-Module SqlServer -DisableNameChecking

$srv = New-Object Microsoft.SqlServer.Management.Smo.Server $Server
$srv.ConnectionContext.TrustServerCertificate = $true
$srv.ConnectionContext.StatementTimeout = 0

$db = $srv.Databases[$Database]
if ($null -eq $db) { throw "Database '$Database' not found on $Server." }

Write-Host "Scripting $Database from $($srv.Information.VersionString) for SQL Server 2019..." -ForegroundColor Cyan

$opts = New-Object Microsoft.SqlServer.Management.Smo.ScriptingOptions

# Target. This is the whole point of the exercise.
$opts.TargetServerVersion = [Microsoft.SqlServer.Management.Smo.SqlServerVersion]::Version150  # 2019
$opts.TargetDatabaseEngineType = [Microsoft.SqlServer.Management.Common.DatabaseEngineType]::Standalone

# Schema and data together, in dependency order so foreign keys land after the
# rows they point at.
$opts.ScriptSchema = $true
$opts.ScriptData = $true
$opts.WithDependencies = $false          # we pass an explicit, ordered list
$opts.IncludeHeaders = $false
$opts.NoCommandTerminator = $false
$opts.AnsiPadding = $true

# Make it re-runnable and self-contained.
$opts.ScriptDrops = $false
$opts.IncludeIfNotExists = $false
$opts.Indexes = $true
$opts.ClusteredIndexes = $true
$opts.NonClusteredIndexes = $true
$opts.DriAll = $true                     # keys, defaults, checks
$opts.Default = $true
$opts.ExtendedProperties = $false
$opts.Triggers = $true

# Identity values must survive - every table here has original ids preserved, and
# the application's registration numbers depend on them.
$opts.NoIdentities = $false

$opts.ToFileOnly = $true
$opts.FileName = $Out
$opts.AppendToFile = $false
$opts.Encoding = [System.Text.Encoding]::UTF8

$scripter = New-Object Microsoft.SqlServer.Management.Smo.Scripter $srv
$scripter.Options = $opts

# Tables first, then the sequence the application needs for new admissions.
$tables = @($db.Tables | Where-Object { -not $_.IsSystemObject } | Sort-Object Name)
Write-Host "  tables: $($tables.Count)" -ForegroundColor Cyan

$urns = New-Object Microsoft.SqlServer.Management.Smo.UrnCollection
foreach ($t in $tables) { $urns.Add($t.Urn) }

<#
    dbo.StudentRegistrationNumber must travel with the data. It is what issues the
    next registration and admission bill number, and it is NOT an identity column,
    so scripting the tables alone leaves a database where the very first attempt to
    admit a student fails with "invalid object name".

    SMO scripts START WITH as the value the sequence was CREATED with, not the one
    it has reached, so the emitted line is rewritten below. Without that, a restored
    copy hands out registration numbers that already belong to real students.
#>
$sequences = @($db.Sequences)
Write-Host "  sequences: $($sequences.Count)" -ForegroundColor Cyan
foreach ($s in $sequences) { $urns.Add($s.Urn) }

$scripter.EnumScript($urns) | Out-Null

# Rewrite START WITH to where the live sequence has actually reached, so the
# restored copy issues the next number rather than replaying used ones.
foreach ($s in $sequences) {
    $s.Refresh()

    <#
        CurrentValue is the last number the sequence actually produced -- EXCEPT
        straight after ALTER SEQUENCE ... RESTART, when nothing has been produced
        yet and it already holds the number the next call will return. Adding the
        increment blindly then overshoots by one, and the restored copy skips a
        registration number that was never issued.

        Decided the same way RegistrationNumberService.PeekAsync decides it: if the
        current value is already on a student, the next one is free; if it is not,
        it is itself the next one.
    #>
    $resume = [int64]$s.CurrentValue + [int64]$s.IncrementValue

    if ($s.Name -eq 'StudentRegistrationNumber') {
        $n = [int64]$s.CurrentValue
        $yy = (Get-Date).ToString('yy')

        # Matches the app's own test: the number, for this year, in any of the
        # registration-id shapes the data actually holds.
        $sql = @"
SELECT COUNT(*) AS Taken FROM dbo.student
WHERE StuRegId = 'AVI/$n/$yy' OR StuRegId = '$n' OR StuRegId LIKE '%/$n/$yy'
"@
        $taken = [int]($db.ExecuteWithResults($sql).Tables[0].Rows[0].Taken)

        if ($taken -eq 0) { $resume = $n }
    }

    $text = Get-Content $Out -Raw
    $pattern = '(?ms)(CREATE SEQUENCE \[dbo\]\.\[' + [regex]::Escape($s.Name) + '\].*?START WITH )(-?\d+)'
    $text = [regex]::Replace($text, $pattern, { param($m) $m.Groups[1].Value + $resume }, 1)
    Set-Content -Path $Out -Value $text -Encoding UTF8 -NoNewline

    Write-Host "  $($s.Name): resumes at $resume" -ForegroundColor Cyan
}

Write-Host "Written: $Out" -ForegroundColor Green
Get-Item $Out | Select-Object Name, @{n='MB'; e={ [math]::Round($_.Length / 1MB, 1) }}
