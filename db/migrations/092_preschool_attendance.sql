-- Avijan PreSchool: attendance for its receptionist and teachers, the same way
-- the Computer Centre takes it — a QR on the office screen or a button on the
-- staff member's own phone, accepted only inside the PreSchool's location
-- fence, and confirmed with a six-digit code emailed to them.
--
-- PreSchoolSetting gains
--   Latitude, Longitude   the centre of the PreSchool's fence
--   GeofenceMetres        how far from it a mark is still accepted
--
-- PreSchoolAttendance           one row per confirmed mark (In or Out)
-- PreSchoolAttendanceChallenge  a mark waiting for its emailed code
--
-- Kept apart from dbo.StaffAttendance and dbo.AttendanceChallenge, which are
-- the Computer Centre's and are not touched. The housekeeper's attendance stays
-- on paper, as the owner asked.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.PreSchoolSetting', 'Latitude') IS NULL
    ALTER TABLE dbo.PreSchoolSetting ADD Latitude DECIMAL(9,6) NULL;
GO
IF COL_LENGTH('dbo.PreSchoolSetting', 'Longitude') IS NULL
    ALTER TABLE dbo.PreSchoolSetting ADD Longitude DECIMAL(9,6) NULL;
GO
IF COL_LENGTH('dbo.PreSchoolSetting', 'GeofenceMetres') IS NULL
    ALTER TABLE dbo.PreSchoolSetting ADD GeofenceMetres INT NOT NULL
        CONSTRAINT DF_PreSchoolSetting_GeofenceMetres DEFAULT (100);
GO

IF OBJECT_ID('dbo.PreSchoolAttendance') IS NULL
    CREATE TABLE dbo.PreSchoolAttendance (
        Id              INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolAttendance PRIMARY KEY,
        StaffId         INT            NOT NULL CONSTRAINT FK_PreSchoolAttendance_Staff REFERENCES dbo.PreSchoolStaff (Id),
        StaffName       NVARCHAR(100)  NULL,
        StaffRole       NVARCHAR(20)   NULL,
        LoginId         VARCHAR(250)   NULL,
        ScannedOn       DATETIME2      NOT NULL,
        OnDate          DATE           NOT NULL,
        Direction       VARCHAR(3)     NOT NULL,
        Latitude        DECIMAL(9,6)   NULL,
        Longitude       DECIMAL(9,6)   NULL,
        AccuracyMetres  DECIMAL(9,2)   NULL,
        DistanceMetres  DECIMAL(9,2)   NULL,
        CreatedOn       DATETIME2      NOT NULL CONSTRAINT DF_PreSchoolAttendance_CreatedOn DEFAULT (SYSDATETIME()),
        CONSTRAINT CK_PreSchoolAttendance_Direction CHECK (Direction IN ('In', 'Out'))
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolAttendance_Staff_Date' AND object_id = OBJECT_ID('dbo.PreSchoolAttendance'))
    CREATE INDEX IX_PreSchoolAttendance_Staff_Date ON dbo.PreSchoolAttendance (StaffId, OnDate, ScannedOn);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolAttendance_Date' AND object_id = OBJECT_ID('dbo.PreSchoolAttendance'))
    CREATE INDEX IX_PreSchoolAttendance_Date ON dbo.PreSchoolAttendance (OnDate);
GO

IF OBJECT_ID('dbo.PreSchoolAttendanceChallenge') IS NULL
    CREATE TABLE dbo.PreSchoolAttendanceChallenge (
        Id              INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolAttendanceChallenge PRIMARY KEY,
        StaffId         INT            NOT NULL CONSTRAINT FK_PreSchoolAttendanceChallenge_Staff REFERENCES dbo.PreSchoolStaff (Id),
        StaffName       NVARCHAR(100)  NULL,
        StaffRole       NVARCHAR(20)   NULL,
        LoginId         VARCHAR(250)   NULL,
        MarkedOn        DATETIME2      NOT NULL,
        OnDate          DATE           NOT NULL,
        Latitude        DECIMAL(9,6)   NULL,
        Longitude       DECIMAL(9,6)   NULL,
        AccuracyMetres  DECIMAL(9,2)   NULL,
        DistanceMetres  DECIMAL(9,2)   NULL,
        CodeHash        VARCHAR(100)   NOT NULL,
        SentToEmail     NVARCHAR(150)  NULL,
        ExpiresOn       DATETIME2      NOT NULL,
        Attempts        INT            NOT NULL CONSTRAINT DF_PreSchoolAttendanceChallenge_Attempts DEFAULT (0),
        ConsumedOn      DATETIME2      NULL,
        CreatedOn       DATETIME2      NOT NULL CONSTRAINT DF_PreSchoolAttendanceChallenge_CreatedOn DEFAULT (SYSDATETIME())
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolAttendanceChallenge_Staff' AND object_id = OBJECT_ID('dbo.PreSchoolAttendanceChallenge'))
    CREATE INDEX IX_PreSchoolAttendanceChallenge_Staff ON dbo.PreSchoolAttendanceChallenge (StaffId, ConsumedOn);
GO
