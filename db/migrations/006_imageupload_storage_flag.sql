/* =====================================================================
   006_imageupload_storage_flag.sql
   Target DB : Avijan_Angular_Migration
   Purpose   : Make ImageUpload say how it stores each file instead of
               forcing every reader to guess.

   dbo.ImageUpload.FilePath holds two different things:

     * a Base64 payload  -- current uploader (HomeController.SaveFiles)
     * a web path such as "../UploadedFiles/<guid>.jpg" -- an earlier
       uploader that wrote the bytes to the web server's disk

   Guessing at read time is fragile: JPEG Base64 begins "/9j/", so any
   "does it contain a slash" test misclassifies every JPEG as a path.
   This adds an explicit IsInline flag, set once, from a rule verified
   against the actual data:

       path rows   : 94 rows, longest  57 bytes, all ending in a file extension
       Base64 rows :  7 rows, shortest 9,244 bytes

   That is a ~160x margin, so length alone is decisive here; the extension
   test is kept as a second condition so a short future upload cannot be
   misfiled.

   Idempotent: safe to re-run.
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE [Avijan_Angular_Migration];
GO

IF COL_LENGTH('dbo.ImageUpload', 'IsInline') IS NULL
    ALTER TABLE dbo.ImageUpload
        ADD IsInline bit NOT NULL CONSTRAINT DF_ImageUpload_IsInline DEFAULT (1);
GO

/* 1 = FilePath is a Base64 payload we can serve.
   0 = FilePath is a disk path; the bytes are not in this database.      */
UPDATE dbo.ImageUpload
SET    IsInline = CASE
                     WHEN FilePath IS NULL THEN 0
                     WHEN DATALENGTH(FilePath) <= 512
                          AND (FilePath LIKE '%.[A-Za-z][A-Za-z][A-Za-z]'
                            OR FilePath LIKE '%.[A-Za-z][A-Za-z][A-Za-z][A-Za-z]')
                     THEN 0
                     ELSE 1
                  END;
GO

/* Backfill ContentType for inline rows that never had one recorded. */
UPDATE dbo.ImageUpload
SET    ContentType = CASE
                        WHEN FilePath LIKE 'iVBOR%' THEN 'image/png'
                        WHEN FilePath LIKE 'R0lGOD%' THEN 'image/gif'
                        WHEN FilePath LIKE 'UklGR%'  THEN 'image/webp'
                        WHEN FilePath LIKE 'JVBER%'  THEN 'application/pdf'
                        ELSE 'image/jpeg'
                     END
WHERE  IsInline = 1
  AND  ContentType IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ImageUpload_StuRegId_IsInline' AND object_id = OBJECT_ID('dbo.ImageUpload'))
    CREATE INDEX IX_ImageUpload_StuRegId_IsInline ON dbo.ImageUpload (StuRegId, IsInline);
GO

PRINT '006_imageupload_storage_flag.sql applied.';

SELECT 'inline (servable) = ' + CAST(COUNT(*) AS varchar) FROM dbo.ImageUpload WHERE IsInline = 1;
SELECT 'disk path only    = ' + CAST(COUNT(*) AS varchar) FROM dbo.ImageUpload WHERE IsInline = 0;
GO
