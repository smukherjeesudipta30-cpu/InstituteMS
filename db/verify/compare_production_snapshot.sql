/* =====================================================================
   compare_production_snapshot.sql

   Proves the local staging mirror (ph19987796653_prod) matches live
   production row for row. Read-only on both sides.

   Every row below must show Prod = Local. A mismatch means the snapshot
   is stale or a copy failed - re-run migrations\011.

   Run:  sqlcmd -S . -I -i verify\compare_production_snapshot.sql -W -s"|"
   ===================================================================== */
SET NOCOUNT ON;
USE ph19987796653_prod;

SELECT  s.TableName,
        s.Prod,
        s.Local,
        CASE WHEN s.Prod = s.Local THEN 'OK' ELSE '*** MISMATCH ***' END AS Status
FROM (
    SELECT 'AttendanceMaster' AS TableName,
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.AttendanceMaster) AS Prod,
           (SELECT COUNT(*) FROM dbo.AttendanceMaster) AS Local
    UNION ALL SELECT 'Branch',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Branch),
           (SELECT COUNT(*) FROM dbo.Branch)
    UNION ALL SELECT 'Course',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Course),
           (SELECT COUNT(*) FROM dbo.Course)
    UNION ALL SELECT 'CusMessage',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.CusMessage),
           (SELECT COUNT(*) FROM dbo.CusMessage)
    UNION ALL SELECT 'dailyRoutine',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.ph19987796653.dailyRoutine),
           (SELECT COUNT(*) FROM dbo.dailyRoutine)
    UNION ALL SELECT 'Expense',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Expense),
           (SELECT COUNT(*) FROM dbo.Expense)
    UNION ALL SELECT 'Fees',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Fees),
           (SELECT COUNT(*) FROM dbo.Fees)
    UNION ALL SELECT 'ImageUpload',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.ImageUpload),
           (SELECT COUNT(*) FROM dbo.ImageUpload)
    UNION ALL SELECT 'LateFees',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.LateFees),
           (SELECT COUNT(*) FROM dbo.LateFees)
    UNION ALL SELECT 'Login',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Login),
           (SELECT COUNT(*) FROM dbo.Login)
    UNION ALL SELECT 'MailLog_registration_time',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.ph19987796653.MailLog_registration_time),
           (SELECT COUNT(*) FROM dbo.MailLog_registration_time)
    UNION ALL SELECT 'Marks',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Marks),
           (SELECT COUNT(*) FROM dbo.Marks)
    UNION ALL SELECT 'monthlyFees',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.monthlyFees),
           (SELECT COUNT(*) FROM dbo.monthlyFees)
    UNION ALL SELECT 'Notice',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Notice),
           (SELECT COUNT(*) FROM dbo.Notice)
    UNION ALL SELECT 'OIncome',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.ph19987796653.OIncome),
           (SELECT COUNT(*) FROM dbo.OIncome)
    UNION ALL SELECT 'student',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.student),
           (SELECT COUNT(*) FROM dbo.student)
    UNION ALL SELECT 'Teacher',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Teacher),
           (SELECT COUNT(*) FROM dbo.Teacher)
    UNION ALL SELECT 'TecExpense',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.ph19987796653.TecExpense),
           (SELECT COUNT(*) FROM dbo.TecExpense)
    UNION ALL SELECT 'TecFees',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.TecFees),
           (SELECT COUNT(*) FROM dbo.TecFees)
    UNION ALL SELECT 'Users',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Users),
           (SELECT COUNT(*) FROM dbo.Users)
) AS s
ORDER BY CASE WHEN s.Prod = s.Local THEN 1 ELSE 0 END, s.TableName;
