SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    052_exam_attempt_violations.sql
    -------------------------------------------------------------------------
    The theory portal now locks the paper into fullscreen and watches for the
    student leaving it (exiting fullscreen, switching tabs, minimising, or
    moving focus to another window). Each such event is a violation, recorded
    against the attempt rather than only shown as a warning on screen, so it
    survives a page reload and can be looked at afterwards -- a warning the
    student can simply dismiss and never account for is not a proctoring
    control.

    ViolationCount -- how many times the student left the exam window during
    this sitting. Incremented server-side on each report from the client, so a
    student cannot reset the count by reloading the page.

    Flagged -- set once ViolationCount reaches the limit the portal enforces
    (currently 3). The portal auto-submits the paper at that point; this bit
    is what marks the resulting attempt as one to look at, distinct from an
    ordinary submission or a time-out.
*/

IF COL_LENGTH('dbo.ExamAttempt', 'ViolationCount') IS NULL
BEGIN
    ALTER TABLE dbo.ExamAttempt
        ADD ViolationCount int NOT NULL CONSTRAINT DF_ExamAttempt_ViolationCount DEFAULT (0);
END
GO

IF COL_LENGTH('dbo.ExamAttempt', 'Flagged') IS NULL
BEGIN
    ALTER TABLE dbo.ExamAttempt
        ADD Flagged bit NOT NULL CONSTRAINT DF_ExamAttempt_Flagged DEFAULT (0);
END
GO
