-- Certificate signature and verification.
--
-- CertificateSetting: one row (Id 1) — the signatory's name and title, and the
-- signature and optional stamp images Admin uploads, printed on every
-- certificate.
--
-- CertificateIssue gains Grade and CompletedOn: what each copy stated, kept as
-- printed so the QR check on the certificate shows the same grade even if marks
-- change later. Copies issued before this have them empty; the check still
-- confirms they were issued.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.CertificateSetting') IS NULL
    CREATE TABLE dbo.CertificateSetting (
        Id                   INT           NOT NULL CONSTRAINT PK_CertificateSetting PRIMARY KEY,
        SignatoryName        NVARCHAR(100) NOT NULL,
        SignatoryTitle       NVARCHAR(100) NOT NULL CONSTRAINT DF_CertificateSetting_Title DEFAULT N'Proprietor',
        SignatureContentType VARCHAR(20)   NULL,
        SignatureImage       NVARCHAR(MAX) NULL,
        StampContentType     VARCHAR(20)   NULL,
        StampImage           NVARCHAR(MAX) NULL,
        ModifiedBy           VARCHAR(250)  NULL,
        ModifiedOn           DATETIME2     NULL,
        CONSTRAINT CK_CertificateSetting_OneRow CHECK (Id = 1)
    );
GO

IF COL_LENGTH('dbo.CertificateIssue', 'Grade') IS NULL
    ALTER TABLE dbo.CertificateIssue ADD Grade VARCHAR(10) NULL;
GO

IF COL_LENGTH('dbo.CertificateIssue', 'CompletedOn') IS NULL
    ALTER TABLE dbo.CertificateIssue ADD CompletedOn DATE NULL;
GO
