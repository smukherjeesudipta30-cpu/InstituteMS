-- A frozen copy of every question on a submitted online theory attempt, with
-- the student's chosen option, so their paper stays readable exactly as sat
-- even if the question bank is later edited or deleted. Written once at
-- submission; the backfill below covers attempts submitted before this existed.
IF OBJECT_ID('dbo.ExamAttemptAnswer') IS NULL
BEGIN
    CREATE TABLE dbo.ExamAttemptAnswer (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamAttemptAnswer PRIMARY KEY,
        AttemptId     INT           NOT NULL,
        QuestionNo    INT           NOT NULL,
        QuestionText  NVARCHAR(MAX) NOT NULL,
        OptionA       NVARCHAR(MAX) NOT NULL,
        OptionB       NVARCHAR(MAX) NOT NULL,
        OptionC       NVARCHAR(MAX) NOT NULL,
        OptionD       NVARCHAR(MAX) NOT NULL,
        CorrectOption CHAR(1)       NOT NULL,
        ChosenOption  CHAR(1)       NULL,
        Marks         INT           NOT NULL,
        IsCorrect     BIT           NOT NULL
    );

    CREATE INDEX IX_ExamAttemptAnswer_AttemptId ON dbo.ExamAttemptAnswer (AttemptId);
END
GO

-- Backfill: every submitted portal attempt with no copy yet, against the
-- questions as they stand now (the closest record there is for those).
;WITH Chosen AS (
    SELECT a.AttemptId,
           TRY_CAST(LEFT(s.value, CHARINDEX(':', s.value) - 1) AS INT) AS QuestionId,
           UPPER(SUBSTRING(s.value, CHARINDEX(':', s.value) + 1, 1))   AS ChosenOption
    FROM dbo.ExamAttempt a
    CROSS APPLY STRING_SPLIT(a.Answers, ',') s
    WHERE a.Source = 'Portal' AND a.SubmittedOn IS NOT NULL
      AND CHARINDEX(':', s.value) > 1
)
INSERT INTO dbo.ExamAttemptAnswer
    (AttemptId, QuestionNo, QuestionText, OptionA, OptionB, OptionC, OptionD,
     CorrectOption, ChosenOption, Marks, IsCorrect)
SELECT a.AttemptId, q.QuestionNo, q.QuestionText, q.OptionA, q.OptionB, COALESCE(q.OptionC, N''), COALESCE(q.OptionD, N''),
       UPPER(q.CorrectOption), c.ChosenOption, q.Marks,
       CASE WHEN c.ChosenOption = UPPER(q.CorrectOption) THEN 1 ELSE 0 END
FROM dbo.ExamAttempt a
JOIN dbo.ExamQuestion q ON q.ExamId = a.ExamId
LEFT JOIN Chosen c ON c.AttemptId = a.AttemptId AND c.QuestionId = q.QuestionId
WHERE a.Source = 'Portal' AND a.SubmittedOn IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.ExamAttemptAnswer x WHERE x.AttemptId = a.AttemptId);
GO
