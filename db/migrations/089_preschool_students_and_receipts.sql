-- Avijan PreSchool, phase 3: admitted children and the money received for them.
--
-- PreSchoolStudent      one child: details, parents, class, joining day, any
--                       monthly discount, and whether they are still studying.
-- PreSchoolReceipt      money received, with its own running receipt number.
-- PreSchoolReceiptLine  what a receipt paid for: the admission fee, or one fee
--                       head for one month, at the amount charged then.
-- Sequences             PreSchoolAdmissionNo and PreSchoolReceiptNo give the
--                       running numbers, so two desks never get the same one.
--
-- Adds tables and sequences only. Nothing in the Computer Centre's students,
-- fees or receipts is touched.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.PreSchoolAdmissionNo', 'SO') IS NULL
    CREATE SEQUENCE dbo.PreSchoolAdmissionNo AS INT START WITH 1 INCREMENT BY 1;
GO

IF OBJECT_ID('dbo.PreSchoolReceiptNo', 'SO') IS NULL
    CREATE SEQUENCE dbo.PreSchoolReceiptNo AS INT START WITH 1 INCREMENT BY 1;
GO

IF OBJECT_ID('dbo.PreSchoolStudent') IS NULL
    CREATE TABLE dbo.PreSchoolStudent (
        Id               INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolStudent PRIMARY KEY,
        AdmissionNo      INT            NOT NULL,
        ChildName        NVARCHAR(100)  NOT NULL,
        DateOfBirth      DATE           NOT NULL,
        Gender           NVARCHAR(10)   NULL,
        BloodGroup       NVARCHAR(5)    NULL,
        FatherName       NVARCHAR(100)  NULL,
        MotherName       NVARCHAR(100)  NULL,
        ParentName       NVARCHAR(100)  NOT NULL,
        Phone            NVARCHAR(20)   NOT NULL,
        EmergencyPhone   NVARCHAR(20)   NULL,
        Email            NVARCHAR(150)  NULL,
        Address          NVARCHAR(400)  NULL,
        ClassId          INT            NOT NULL CONSTRAINT FK_PreSchoolStudent_Class REFERENCES dbo.PreSchoolClass (Id),
        JoinedOn         DATE           NOT NULL,
        MonthlyDiscount  DECIMAL(10,2)  NOT NULL CONSTRAINT DF_PreSchoolStudent_MonthlyDiscount DEFAULT (0),
        Status           NVARCHAR(20)   NOT NULL CONSTRAINT DF_PreSchoolStudent_Status DEFAULT ('Active'),
        EndedOn          DATE           NULL,
        Notes            NVARCHAR(500)  NULL,
        CreatedBy        VARCHAR(250)   NULL,
        CreatedOn        DATETIME2      NOT NULL CONSTRAINT DF_PreSchoolStudent_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy       VARCHAR(250)   NULL,
        ModifiedOn       DATETIME2      NULL,
        CONSTRAINT UQ_PreSchoolStudent_AdmissionNo UNIQUE (AdmissionNo),
        CONSTRAINT CK_PreSchoolStudent_Discount CHECK (MonthlyDiscount >= 0),
        CONSTRAINT CK_PreSchoolStudent_Status CHECK (Status IN ('Active', 'Completed', 'Left'))
    );
GO

IF OBJECT_ID('dbo.PreSchoolReceipt') IS NULL
    CREATE TABLE dbo.PreSchoolReceipt (
        Id           INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolReceipt PRIMARY KEY,
        ReceiptNo    INT            NOT NULL,
        StudentId    INT            NOT NULL CONSTRAINT FK_PreSchoolReceipt_Student REFERENCES dbo.PreSchoolStudent (Id),
        ReceivedOn   DATE           NOT NULL,
        Mode         NVARCHAR(10)   NOT NULL,
        Reference    NVARCHAR(60)   NULL,
        Total        DECIMAL(10,2)  NOT NULL,
        Notes        NVARCHAR(300)  NULL,
        IsCancelled  BIT            NOT NULL CONSTRAINT DF_PreSchoolReceipt_IsCancelled DEFAULT (0),
        CancelledBy  VARCHAR(250)   NULL,
        CancelledOn  DATETIME2      NULL,
        CreatedBy    VARCHAR(250)   NULL,
        CreatedOn    DATETIME2      NOT NULL CONSTRAINT DF_PreSchoolReceipt_CreatedOn DEFAULT (SYSDATETIME()),
        CONSTRAINT UQ_PreSchoolReceipt_ReceiptNo UNIQUE (ReceiptNo),
        CONSTRAINT CK_PreSchoolReceipt_Mode CHECK (Mode IN ('Cash', 'UPI', 'Paytm'))
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolReceipt_ReceivedOn' AND object_id = OBJECT_ID('dbo.PreSchoolReceipt'))
    CREATE INDEX IX_PreSchoolReceipt_ReceivedOn ON dbo.PreSchoolReceipt (ReceivedOn);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolReceipt_StudentId' AND object_id = OBJECT_ID('dbo.PreSchoolReceipt'))
    CREATE INDEX IX_PreSchoolReceipt_StudentId ON dbo.PreSchoolReceipt (StudentId);
GO

IF OBJECT_ID('dbo.PreSchoolReceiptLine') IS NULL
    CREATE TABLE dbo.PreSchoolReceiptLine (
        Id           INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolReceiptLine PRIMARY KEY,
        ReceiptId    INT            NOT NULL CONSTRAINT FK_PreSchoolReceiptLine_Receipt REFERENCES dbo.PreSchoolReceipt (Id) ON DELETE CASCADE,
        Kind         NVARCHAR(10)   NOT NULL,
        ForMonth     DATE           NULL,
        Description  NVARCHAR(120)  NOT NULL,
        Amount       DECIMAL(10,2)  NOT NULL,
        CONSTRAINT CK_PreSchoolReceiptLine_Kind CHECK (Kind IN ('Admission', 'Monthly'))
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolReceiptLine_ReceiptId' AND object_id = OBJECT_ID('dbo.PreSchoolReceiptLine'))
    CREATE INDEX IX_PreSchoolReceiptLine_ReceiptId ON dbo.PreSchoolReceiptLine (ReceiptId);
GO
