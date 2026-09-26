-- Uploaded answer scripts (photos or a PDF scan) for theory papers sat
-- offline. At least one must be on file before the paper's mark can be
-- entered. Stored in the database, like student photos.
IF OBJECT_ID('dbo.ExamAnswerCopy') IS NULL
BEGIN
    CREATE TABLE dbo.ExamAnswerCopy (
        Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamAnswerCopy PRIMARY KEY,
        ExamId      INT            NOT NULL,
        StuRegId    VARCHAR(50)    NOT NULL,
        FileName    NVARCHAR(200)  NOT NULL,
        ContentType VARCHAR(100)   NOT NULL,
        Content     VARBINARY(MAX) NOT NULL,
        SizeBytes   INT            NOT NULL,
        UploadedBy  VARCHAR(250)   NULL,
        UploadedOn  DATETIME2      NOT NULL
    );

    CREATE INDEX IX_ExamAnswerCopy_Exam_Student ON dbo.ExamAnswerCopy (ExamId, StuRegId);
END
GO
