/* =====================================================================
   compare_production_to_target.sql

   End-to-end proof that the application database holds everything
   production holds. Compares LIVE production against
   Avijan_Angular_Migration, not against the staging mirror, so a fault
   anywhere in the chain shows up here.

   Expected differences are stated, not hidden:
     Course   +1  placeholder for a deleted course still referenced
     Teacher  +n  placeholders for deleted teachers still referenced
     Branch    -  excluded on purpose; empty on production
     Users     +1  the local administrator account

   Run:  sqlcmd -S . -I -i verify\compare_production_to_target.sql -W -s"|"
   ===================================================================== */
SET NOCOUNT ON;
USE Avijan_Angular_Migration;

SELECT  x.TableName, x.Prod, x.Local, x.Expected,
        CASE WHEN x.Local = x.Prod + x.Expected THEN 'OK'
             ELSE '*** CHECK ***' END AS Status
FROM (
    SELECT 'Course' AS TableName,
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Course) AS Prod,
           (SELECT COUNT(*) FROM dbo.Course) AS Local,
           (SELECT COUNT(*) FROM dbo.Course WHERE CreatedBy = 'migration-004-orphan') AS Expected
    UNION ALL SELECT 'Teacher',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Teacher),
           (SELECT COUNT(*) FROM dbo.Teacher),
           (SELECT COUNT(*) FROM dbo.Teacher WHERE CreatedBy = 'migration-004-orphan')
    UNION ALL SELECT 'Student',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.student),
           (SELECT COUNT(*) FROM dbo.Student), 0
    UNION ALL SELECT 'ImageUpload',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.ImageUpload),
           (SELECT COUNT(*) FROM dbo.ImageUpload), 0
    UNION ALL SELECT 'MonthlyFees',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.monthlyFees),
           (SELECT COUNT(*) FROM dbo.MonthlyFees), 0
    UNION ALL SELECT 'LateFees',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.LateFees),
           (SELECT COUNT(*) FROM dbo.LateFees), 0
    UNION ALL SELECT 'TecFees',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.TecFees),
           (SELECT COUNT(*) FROM dbo.TecFees), 0
    UNION ALL SELECT 'TecExpense',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.ph19987796653.TecExpense),
           (SELECT COUNT(*) FROM dbo.TecExpense), 0
    UNION ALL SELECT 'Fees',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Fees),
           (SELECT COUNT(*) FROM dbo.Fees), 0
    UNION ALL SELECT 'Expense',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Expense),
           (SELECT COUNT(*) FROM dbo.Expense), 0
    UNION ALL SELECT 'OIncome',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.ph19987796653.OIncome),
           (SELECT COUNT(*) FROM dbo.OIncome), 0
    UNION ALL SELECT 'Marks',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Marks),
           (SELECT COUNT(*) FROM dbo.Marks), 0
    UNION ALL SELECT 'Notice',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Notice),
           (SELECT COUNT(*) FROM dbo.Notice), 0
    UNION ALL SELECT 'CusMessage',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.CusMessage),
           (SELECT COUNT(*) FROM dbo.CusMessage), 0
    UNION ALL SELECT 'Users',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Users),
           (SELECT COUNT(*) FROM dbo.Users),
           (SELECT COUNT(*) FROM dbo.Users WHERE Email = 'admin@avijan.local')
    UNION ALL SELECT 'AttendanceMaster',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.AttendanceMaster),
           (SELECT COUNT(*) FROM dbo.AttendanceMaster), 0
    UNION ALL SELECT 'dailyRoutine',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.ph19987796653.dailyRoutine),
           (SELECT COUNT(*) FROM dbo.dailyRoutine), 0
    UNION ALL SELECT 'MailLog_registration_time',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.ph19987796653.MailLog_registration_time),
           (SELECT COUNT(*) FROM dbo.MailLog_registration_time), 0
    UNION ALL SELECT 'Login',
           (SELECT COUNT(*) FROM AVIJAN_PROD.ph19987796653_.dbo.Login),
           (SELECT COUNT(*) FROM dbo.Login), 0
) AS x
ORDER BY CASE WHEN x.Local = x.Prod + x.Expected THEN 1 ELSE 0 END, x.TableName;
