-- Teacher competency exam portal: Admin assigns a teacher to a course, sets a
-- sitting's date/time, the system generates MCQs for it, and the teacher takes
-- it. See InstituteMS.Domain.Entities.TeacherExamEntities.

-- No FK to dbo.Teacher/dbo.Course: neither carries a primary or unique key in
-- the legacy schema (matches every other table that references them, e.g.
-- dbo.TecFees.TecId), so a DB-level FK constraint cannot be created here.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TeacherExamAssignment')
CREATE TABLE dbo.TeacherExamAssignment (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TeacherId INT NOT NULL,
    CourseId INT NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_TeacherExamAssignment_IsActive DEFAULT 1,
    CreatedBy NVARCHAR(50) NULL,
    CreatedOn DATETIME2 NOT NULL,
    ModifiedBy NVARCHAR(50) NULL,
    ModifiedOn DATETIME2 NULL,
    CONSTRAINT UQ_TeacherExamAssignment_Teacher_Course UNIQUE (TeacherId, CourseId)
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TeacherExamSchedule')
CREATE TABLE dbo.TeacherExamSchedule (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TeacherExamAssignmentId INT NOT NULL,
    ScheduledOn DATETIME2 NOT NULL,
    CreatedBy NVARCHAR(50) NULL,
    CreatedOn DATETIME2 NOT NULL,
    CONSTRAINT FK_TeacherExamSchedule_Assignment FOREIGN KEY (TeacherExamAssignmentId)
        REFERENCES dbo.TeacherExamAssignment(Id) ON DELETE CASCADE
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TeacherExamQuestion')
CREATE TABLE dbo.TeacherExamQuestion (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TeacherExamScheduleId INT NOT NULL,
    OrderNo INT NOT NULL,
    QuestionText NVARCHAR(1000) NOT NULL,
    OptionA NVARCHAR(500) NOT NULL,
    OptionB NVARCHAR(500) NOT NULL,
    OptionC NVARCHAR(500) NOT NULL,
    OptionD NVARCHAR(500) NOT NULL,
    CorrectOption NVARCHAR(1) NOT NULL,
    CreatedOn DATETIME2 NOT NULL,
    CONSTRAINT FK_TeacherExamQuestion_Schedule FOREIGN KEY (TeacherExamScheduleId)
        REFERENCES dbo.TeacherExamSchedule(Id) ON DELETE CASCADE
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TeacherExamAttempt')
CREATE TABLE dbo.TeacherExamAttempt (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TeacherExamScheduleId INT NOT NULL,
    StartedOn DATETIME2 NOT NULL,
    SubmittedOn DATETIME2 NULL,
    ScorePercent DECIMAL(5,2) NULL,
    Passed BIT NULL,
    CONSTRAINT FK_TeacherExamAttempt_Schedule FOREIGN KEY (TeacherExamScheduleId)
        REFERENCES dbo.TeacherExamSchedule(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_TeacherExamAttempt_Schedule UNIQUE (TeacherExamScheduleId)
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TeacherExamAnswer')
CREATE TABLE dbo.TeacherExamAnswer (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TeacherExamAttemptId INT NOT NULL,
    TeacherExamQuestionId INT NOT NULL,
    SelectedOption NVARCHAR(1) NULL,
    IsCorrect BIT NOT NULL CONSTRAINT DF_TeacherExamAnswer_IsCorrect DEFAULT 0,
    CONSTRAINT FK_TeacherExamAnswer_Attempt FOREIGN KEY (TeacherExamAttemptId)
        REFERENCES dbo.TeacherExamAttempt(Id) ON DELETE CASCADE,
    CONSTRAINT FK_TeacherExamAnswer_Question FOREIGN KEY (TeacherExamQuestionId)
        REFERENCES dbo.TeacherExamQuestion(Id),
    CONSTRAINT UQ_TeacherExamAnswer_Attempt_Question UNIQUE (TeacherExamAttemptId, TeacherExamQuestionId)
);
