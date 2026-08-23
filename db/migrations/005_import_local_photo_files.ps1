<#
    005_import_local_photo_files.ps1
    Target DB : Avijan_Angular_Migration

    Why this is a PowerShell script and not a .sql file
    ---------------------------------------------------
    dbo.ImageUpload holds two generations of data:

      * 3 rows store a Base64 payload   -- written by the current uploader
                                           (HomeController.SaveFiles)
      * 98 rows store a web path such as "/UploadedFiles/<guid>.jpg" -- written by
        an earlier uploader that saved the bytes to the web server's disk and kept
        only the path in the database.

    The bytes behind those 98 paths are NOT in the database, so 004 could not bring
    them across. This script recovers the ones that exist in the backup's
    UploadedFiles folder by reading each file and storing it as Base64, which is the
    format the API serves. Photos still living only on the production web server
    cannot be recovered from here -- copy that folder onto this machine and re-run.

    Idempotent: rows already holding Base64 are skipped.

    Usage:
        powershell -File db\migrations\005_import_local_photo_files.ps1
        powershell -File db\migrations\005_import_local_photo_files.ps1 -UploadRoot "D:\avijan\UploadedFiles"
#>

[CmdletBinding()]
param(
    [string] $Server       = '.',
    [string] $Database     = 'Avijan_Angular_Migration',
    [string] $UploadRoot   = 'E:\backup_TestAngularProject - 31052026\TestAngularProject\TestAngularProject\UploadedFiles',
    [switch] $WhatIfOnly
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $UploadRoot)) {
    Write-Warning "Upload folder not found: $UploadRoot"
    Write-Warning 'Nothing to import. Point -UploadRoot at the legacy UploadedFiles folder and re-run.'
    return
}

$connectionString = "Server=$Server;Database=$Database;Integrated Security=SSPI;TrustServerCertificate=True"

Add-Type -AssemblyName System.Data

$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
$connection.Open()

try {
    # Candidate rows: FilePath still looks like a path rather than a Base64 payload.
    $select = $connection.CreateCommand()
    $select.CommandText = @'
SELECT FileID, FileName, StuRegId, FilePath
FROM   dbo.ImageUpload
WHERE  FilePath IS NOT NULL
  AND  (FilePath LIKE '%/%' OR FilePath LIKE '%\%')
'@

    $rows = @()
    $reader = $select.ExecuteReader()
    while ($reader.Read()) {
        $rows += [pscustomobject]@{
            FileID   = $reader.GetInt32(0)
            FileName = if ($reader.IsDBNull(1)) { $null } else { $reader.GetString(1) }
            StuRegId = if ($reader.IsDBNull(2)) { $null } else { $reader.GetString(2) }
            FilePath = $reader.GetString(3)
        }
    }
    $reader.Close()

    Write-Host "Path-style photo rows found : $($rows.Count)"

    $imported = 0
    $missing  = 0

    foreach ($row in $rows) {
        # "/UploadedFiles/<guid>.jpg"  ->  "<guid>.jpg"
        $leaf = Split-Path -Leaf $row.FilePath.Replace('\', '/')
        $full = Join-Path $UploadRoot $leaf

        if (-not (Test-Path -LiteralPath $full)) {
            $missing++
            continue
        }

        $bytes  = [System.IO.File]::ReadAllBytes($full)
        $base64 = [Convert]::ToBase64String($bytes)

        $extension   = [System.IO.Path]::GetExtension($leaf).ToLowerInvariant()
        $contentType = switch ($extension) {
            '.png'  { 'image/png' }
            '.gif'  { 'image/gif' }
            '.bmp'  { 'image/bmp' }
            '.webp' { 'image/webp' }
            '.pdf'  { 'application/pdf' }
            default { 'image/jpeg' }
        }

        Write-Host ("  {0,-40} {1,10:N0} bytes -> {2}" -f $leaf, $bytes.Length, $row.StuRegId)

        if ($WhatIfOnly) { $imported++; continue }

        $update = $connection.CreateCommand()
        $update.CommandText = @'
UPDATE dbo.ImageUpload
SET    FilePath    = @FilePath,
       FileSize    = @FileSize,
       ContentType = @ContentType,
       FileName    = ISNULL(FileName, @FileName)
WHERE  FileID      = @FileID
'@
        $null = $update.Parameters.AddWithValue('@FilePath',    $base64)
        $null = $update.Parameters.AddWithValue('@FileSize',    $bytes.Length)
        $null = $update.Parameters.AddWithValue('@ContentType', $contentType)
        $null = $update.Parameters.AddWithValue('@FileName',    $leaf)
        $null = $update.Parameters.AddWithValue('@FileID',      $row.FileID)
        $null = $update.ExecuteNonQuery()

        $imported++
    }

    Write-Host ''
    Write-Host "Imported : $imported"
    Write-Host "Still only on the production web server : $missing"

    if ($missing -gt 0) {
        Write-Host ''
        Write-Warning "$missing photo(s) reference files that are not in '$UploadRoot'."
        Write-Warning 'Copy the production UploadedFiles folder here and re-run to recover them.'
    }
}
finally {
    $connection.Close()
}
