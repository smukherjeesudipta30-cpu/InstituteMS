/* =====================================================================
   002_seed_reference_data.sql
   Target DB : Avijan_Angular_Migration
   Purpose   : Sample reference data so the Home Dashboard renders real
               numbers before the full data migration runs.
               Safe to delete once real data is imported from
               ph19987796653_.
   Idempotent: only inserts rows that are not already present.
   Note      : The Admin user is NOT seeded here — it is created by the
               API on startup (Infrastructure/Persistence/DbSeeder.cs) so
               the password is hashed with BCrypt rather than pinned to a
               hard-coded hash in source control.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE [Avijan_Angular_Migration];
GO

/* ---------------------------------------------------------------- Course */
MERGE dbo.Course AS t
USING (VALUES
    ('Tabla',            36),
    ('Vocal Classical',  48),
    ('Guitar',           24),
    ('Keyboard',         24),
    ('Drawing',          12)
) AS s (CName, Duration)
    ON t.CName = s.CName
WHEN NOT MATCHED BY TARGET THEN
    INSERT (CName, Duration, CreatedBy) VALUES (s.CName, s.Duration, 'seed');
GO

/* --------------------------------------------------------------- Teacher */
MERGE dbo.Teacher AS t
USING (VALUES
    ('A. Chatterjee', 'achatterjee', '9830000001'),
    ('S. Das',        'sdas',        '9830000002'),
    ('R. Mukherjee',  'rmukherjee',  '9830000003')
) AS s (TecName, LoginId, TecPhone)
    ON t.LoginId = s.LoginId
WHEN NOT MATCHED BY TARGET THEN
    INSERT (TecName, LoginId, TecPhone, CreatedBy) VALUES (s.TecName, s.LoginId, s.TecPhone, 'seed');
GO

/* --------------------------------------------------------------- Student */
DECLARE @yy varchar(2) = RIGHT(CONVERT(varchar(4), YEAR(GETDATE())), 2);

MERGE dbo.Student AS t
USING (
    SELECT  s.StuName, s.GName, s.StuPhone, s.StuEmail,
            'AVI/' + CAST(s.n AS varchar(5)) + '/' + @yy      AS StuRegId,
            'S-A-' + CAST(s.n AS varchar(5)) + '-' + @yy      AS BillNo,
            s.AdFees, s.MonthlyFees, s.DOJ, s.DOB, s.CName, s.IsActive
    FROM (VALUES
        (1, 'Ananya Ghosh',   'Bimal Ghosh',   '9830100001', 'ananya@example.com',  3000.00,  800.00, DATEADD(month, -10, CAST(GETDATE() AS date)), '2008-04-12', 'Tabla',           CAST(1 AS bit)),
        (2, 'Rahul Sen',      'Kamal Sen',     '9830100002', 'rahul@example.com',   3000.00,  900.00, DATEADD(month,  -8, CAST(GETDATE() AS date)), '2007-11-03', 'Vocal Classical', CAST(1 AS bit)),
        (3, 'Priya Roy',      'Nikhil Roy',    '9830100003', 'priya@example.com',   2500.00,  750.00, DATEADD(month,  -6, CAST(GETDATE() AS date)), '2009-01-25', 'Guitar',          CAST(1 AS bit)),
        (4, 'Soham Dutta',    'Arun Dutta',    '9830100004', 'soham@example.com',   2500.00,  750.00, DATEADD(month,  -5, CAST(GETDATE() AS date)), '2006-07-19', 'Keyboard',        CAST(1 AS bit)),
        (5, 'Ishita Paul',    'Manas Paul',    '9830100005', 'ishita@example.com',  2000.00,  600.00, DATEADD(month,  -3, CAST(GETDATE() AS date)), '2010-02-08', 'Drawing',         CAST(1 AS bit)),
        (6, 'Arjun Bose',     'Tapan Bose',    '9830100006', 'arjun@example.com',   3000.00,  800.00, DATEADD(month,  -2, CAST(GETDATE() AS date)), '2005-09-30', 'Tabla',           CAST(1 AS bit)),
        (7, 'Meghna Saha',    'Dipak Saha',    '9830100007', 'meghna@example.com',  2500.00,  700.00, DATEADD(month, -24, CAST(GETDATE() AS date)), '2004-05-14', 'Guitar',          CAST(0 AS bit))
    ) AS s (n, StuName, GName, StuPhone, StuEmail, AdFees, MonthlyFees, DOJ, DOB, CName, IsActive)
) AS s
    ON t.StuRegId = s.StuRegId
WHEN NOT MATCHED BY TARGET THEN
    INSERT (StuName, GName, StuPhone, StuEmail, StuRegId, BillNo, StuAdFees,
            Stu_monthly_fees, DOJ, DOB, Cid, NoTech, IsActive, CreatedBy)
    VALUES (s.StuName, s.GName, s.StuPhone, s.StuEmail, s.StuRegId, s.BillNo, s.AdFees,
            s.MonthlyFees, s.DOJ, s.DOB,
            (SELECT TOP 1 c.Cid FROM dbo.Course c WHERE c.CName = s.CName), 1, s.IsActive, 'seed');
GO

/* Assign a primary teacher to every seeded student that has none. */
UPDATE s
   SET s.TecName = (SELECT TOP 1 t.id FROM dbo.Teacher t ORDER BY t.id)
  FROM dbo.Student s
 WHERE s.CreatedBy = 'seed' AND s.TecName IS NULL;
GO

/* ---------------------------------------------------- MonthlyFees ledger */
/* One payment per active student for each of the last 6 months. */
IF NOT EXISTS (SELECT 1 FROM dbo.MonthlyFees WHERE CreatedBy = 'seed')
BEGIN
    ;WITH months AS (
        SELECT 0 AS m
        UNION ALL SELECT m + 1 FROM months WHERE m < 5
    )
    INSERT INTO dbo.MonthlyFees (StuRegId, BillNo, Date1, PDate, Installment, OInstallment, percen, OldDue, NewDue, CreatedBy)
    SELECT  s.StuRegId,
            'M-' + REPLACE(s.StuRegId, '/', '-') + '-' + CAST(mo.m AS varchar(2)),
            DATEADD(month, -mo.m, CAST(GETDATE() AS date)),
            DATEADD(month, -mo.m, CAST(GETDATE() AS date)),
            s.Stu_monthly_fees,
            0, 0, 0, 0,
            'seed'
      FROM dbo.Student s
     CROSS JOIN months mo
     WHERE s.IsActive = 1
       AND s.CreatedBy = 'seed'
       AND DATEADD(month, -mo.m, CAST(GETDATE() AS date)) >= s.DOJ;
END
GO

/* --------------------------------------------------------------- Expense */
IF NOT EXISTS (SELECT 1 FROM dbo.Expense WHERE CreatedBy = 'seed')
BEGIN
    INSERT INTO dbo.Expense (ExDetails, ExDate, Amount, CreatedBy) VALUES
        ('Electricity bill',        DATEADD(month, -0, CAST(GETDATE() AS date)), 2400.00, 'seed'),
        ('Studio rent',             DATEADD(month, -0, CAST(GETDATE() AS date)), 9000.00, 'seed'),
        ('Instrument maintenance',  DATEADD(month, -1, CAST(GETDATE() AS date)), 1500.00, 'seed'),
        ('Stationery & printing',   DATEADD(month, -1, CAST(GETDATE() AS date)),  850.00, 'seed'),
        ('Internet',                DATEADD(month, -2, CAST(GETDATE() AS date)), 1100.00, 'seed');
END
GO

/* --------------------------------------------------------------- OIncome */
IF NOT EXISTS (SELECT 1 FROM dbo.OIncome WHERE CreatedBy = 'seed')
BEGIN
    INSERT INTO dbo.OIncome (IPurpose, IAmount, ODate, CreatedBy) VALUES
        ('Annual function ticket sales', 12000.00, DATEADD(month, -1, CAST(GETDATE() AS date)), 'seed'),
        ('Workshop fee',                  4500.00, DATEADD(month, -0, CAST(GETDATE() AS date)), 'seed');
END
GO

/* ---------------------------------------------------------------- Notice */
IF NOT EXISTS (SELECT 1 FROM dbo.Notice WHERE CreatedBy = 'seed')
BEGIN
    INSERT INTO dbo.Notice (NewNotice, NoticeDate, CreatedBy) VALUES
        ('Annual examination schedule will be published on the notice board next week.', SYSDATETIME(), 'seed'),
        ('The institute will remain closed on public holidays. Make-up classes on Sunday.', DATEADD(day, -6, SYSDATETIME()), 'seed'),
        ('Monthly fees for the current month are due by the 10th.', DATEADD(day, -14, SYSDATETIME()), 'seed');
END
GO

PRINT '002_seed_reference_data.sql applied.';
GO
