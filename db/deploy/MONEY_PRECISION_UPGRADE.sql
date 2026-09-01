/* =====================================================================
   MONEY_PRECISION_UPGRADE.sql
   ---------------------------------------------------------------------
   Gives every money column in the live legacy database two decimal
   places, so amounts can hold paise.

   Production's money columns are all decimal(18,0) and nullable -
   verified against the live database, not inferred. Each becomes
   decimal(18,2): the precision is kept exactly as it is and only the
   scale moves, which is the smallest change that fixes the problem.

   WHY THIS IS A SEPARATE SCRIPT
   ---------------------------------------------------------------------
   LEGACY_COEXIST_UPGRADE.sql is guaranteed to touch nothing the legacy
   application uses. This script deliberately breaks that guarantee: it
   alters live columns. It is kept apart so the safe script stays safe,
   and so this one can be scheduled, backed up and rolled back on its own.

   WHAT CHANGES FOR THE LEGACY APPLICATION
   ---------------------------------------------------------------------
   One thing, and it is cosmetic: amounts render as "100.00" instead of
   "100". Every legacy read is sdr["Amount"].ToString() into a string
   property (Models/Expense.cs, Income.cs, CoName.cs, Due.cs,
   Latefine.cs, and the three report controllers), so the extra decimals
   flow straight to the screen and to printed receipts.

   Nothing else was found to break, and it was looked for specifically:

     * No Convert.ToInt32 / int.Parse on any money field. The only
       Convert.ToInt32 calls are on TotalMonths, an int column that is
       not touched here.
     * No string equality test on an amount anywhere in C# or in the
       application's own JavaScript.
     * Money in JavaScript goes through parseFloat, never parseInt. The
       only parseInt calls are a digit-array number-to-words routine and
       $scope.NoTech.
     * Scripts/MonthlyCourse.js already renders late fees and
       instalments with .toFixed(2), so two decimals are not new to
       this UI.
     * Writes already pass through Convert.ToDecimal, so they keep
       working - and stop rounding. Today 100.50 is stored as 101.

   Column COUNTS are unchanged, so the seven legacy "INSERT INTO t
   VALUES (...)" statements with no column list are unaffected.

   OVERFLOW
   ---------------------------------------------------------------------
   Holding precision at 18 and moving the scale to 2 spends two of the
   integer digits on decimals: the ceiling falls from 999,999,999,999,999,999
   to 9,999,999,999,999,999.99 - sixteen integer digits instead of eighteen.
   Production's largest value anywhere is student.StuAdFees at 204,546,
   which is six digits, so the headroom is absurd either way.

   It is still checked rather than assumed. Every column is measured
   against its own data before anything is altered, using a ceiling
   derived from that column's real precision, and the script stops
   without touching a single column if any value would not survive.

   LOCKING
   ---------------------------------------------------------------------
   ALTER COLUMN rewrites the table under a schema-modification lock. At
   these volumes (8,656 monthlyFees, 953 student, 833 Expense) that is
   sub-second, but it does block the legacy application while it runs.
   RUN IT WHEN NOBODY IS USING THE SYSTEM.

   No money column participates in any index, so nothing is rebuilt.

   HOW TO RUN
   ---------------------------------------------------------------------
       sqlcmd -S <server> -d ph19987796653_ -U <user> -P <password> -I -b ^
              -i MONEY_PRECISION_UPGRADE.sql -o money_log.txt

   In SSMS: select the database and turn ON SQLCMD Mode (Query -> SQLCMD
   Mode) before executing.

   TAKE A BACKUP FIRST. This one alters live columns - the backup is the
   rollback. Re-running is harmless; a column already at scale 2 is
   skipped.
   ===================================================================== */

:on error exit

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

/* ============================================ 0. preflight ========== */

PRINT '=====================================================';
PRINT CONCAT('Target database : ', DB_NAME());
PRINT CONCAT('Server          : ', @@SERVERNAME);
PRINT CONCAT('Started         : ', CONVERT(varchar(19), SYSDATETIME(), 120));
PRINT '=====================================================';
GO

IF COL_LENGTH('dbo.Branch', 'Branch_name') IS NULL
   OR COL_LENGTH('dbo.Users', 'Password') IS NULL
BEGIN
    RAISERROR ('This does not look like the LEGACY schema (expected Branch.Branch_name and Users.Password). Run this only against the live legacy database.', 16, 1);
END
GO

PRINT 'Preflight passed - legacy schema confirmed.';
GO

/* ================================= 1. the columns being widened =====
   student.Stu_monthly_fees and monthlyFees.stu_actual_monthly_fees are
   deliberately NOT here. They are varchar, so they already hold "1500.50"
   as text and never rounded anything. Converting text to numeric is a
   different and riskier change, and it is not needed to fix paise. */

IF OBJECT_ID('tempdb..#money') IS NOT NULL DROP TABLE #money;

CREATE TABLE #money
(
    TblName varchar(128) NOT NULL,
    ColName varchar(128) NOT NULL
);

INSERT INTO #money (TblName, ColName) VALUES
    ('Expense',     'Amount'),
    ('OIncome',     'IAmount'),
    ('Fees',        'Total'),
    ('LateFees',    'FineFees'),
    ('monthlyFees', 'Installment'),
    ('monthlyFees', 'OInstallment'),
    ('monthlyFees', 'NewDue'),
    ('monthlyFees', 'OldDue'),
    ('monthlyFees', 'percen'),
    ('student',     'StuAdFees'),
    ('TecFees',     'TecSal'),
    ('TecExpense',  'Amount');
GO

PRINT '';
PRINT '--- current state ---';
GO

SELECT TableName = m.TblName,
       ColumnName = m.ColName,
       CurrentType = CASE
            WHEN c.name IS NULL THEN '*** COLUMN NOT FOUND ***'
            ELSE CONCAT(ty.name, '(', c.precision, ',', c.scale, ')',
                        CASE WHEN c.is_nullable = 1 THEN ' null' ELSE ' not null' END)
       END
FROM #money m
LEFT JOIN sys.columns c ON c.object_id = OBJECT_ID('dbo.' + m.TblName) AND c.name = m.ColName
LEFT JOIN sys.types  ty ON ty.user_type_id = c.user_type_id
ORDER BY m.TblName, m.ColName;
GO

/* Totals taken BEFORE anything is altered. Widening scale changes how a
   number prints, not what it is worth, so the same figures must come
   back out at the end. They are printed again in step 4. */
PRINT '';
PRINT '--- totals BEFORE ---';
GO

SELECT Measure = 'Expense total',     Value = (SELECT SUM(Amount)      FROM dbo.Expense)
UNION ALL SELECT 'OIncome total',              (SELECT SUM(IAmount)     FROM dbo.OIncome)
UNION ALL SELECT 'monthlyFees Installment',    (SELECT SUM(Installment) FROM dbo.monthlyFees)
UNION ALL SELECT 'student StuAdFees',          (SELECT SUM(StuAdFees)   FROM dbo.student)
UNION ALL SELECT 'TecFees TecSal',             (SELECT SUM(TecSal)      FROM dbo.TecFees);
GO

/* ============================ 2. overflow check BEFORE altering ======
   Every column is measured against its own data first. If a single value
   would not survive the change, the script stops here and no column has
   been touched yet. */

DECLARE @tbl varchar(128), @col varchar(128), @sql nvarchar(max);
DECLARE @prec int, @maxval decimal(38,4), @ceiling decimal(38,4), @overflow int = 0;

DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT m.TblName, m.ColName, c.precision
    FROM #money m
    JOIN sys.columns c ON c.object_id = OBJECT_ID('dbo.' + m.TblName) AND c.name = m.ColName
    WHERE c.scale = 0;

OPEN cur;
FETCH NEXT FROM cur INTO @tbl, @col, @prec;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @maxval = NULL;

    /* Keeping the precision and moving the scale to 2 leaves
       (precision - 2) integer digits, so this is the largest whole
       number the column will still hold afterwards. */
    SET @ceiling = POWER(CAST(10 AS decimal(38,4)), @prec - 2) - 1;

    SET @sql = N'SELECT @m = MAX(ABS(CAST([' + @col + N'] AS decimal(38,4)))) FROM dbo.[' + @tbl + N']';

    EXEC sp_executesql @sql, N'@m decimal(38,4) OUTPUT', @m = @maxval OUTPUT;

    IF @maxval > @ceiling
    BEGIN
        PRINT CONCAT('*** OVERFLOW: ', @tbl, '.', @col, ' holds ', @maxval,
                     ' but decimal(', @prec, ',2) tops out at ', @ceiling, '.');
        SET @overflow = @overflow + 1;
    END
    ELSE
        PRINT CONCAT('ok: ', @tbl, '.', @col, ' is decimal(', @prec, ',0), max = ',
                     ISNULL(CAST(@maxval AS varchar(40)), 'no rows'),
                     ', ceiling after change = ', @ceiling);

    FETCH NEXT FROM cur INTO @tbl, @col, @prec;
END

CLOSE cur;
DEALLOCATE cur;

IF @overflow > 0
    RAISERROR ('One or more columns hold a value too large to keep once two digits move to the right of the point. NOTHING has been altered. Raise the precision as well as the scale for those columns, or correct the data first.', 16, 1);
ELSE
    PRINT 'Overflow check passed - every value survives the change.';
GO

/* ================================= 3. widen ==========================
   Nullability is read from the column and restated, because ALTER COLUMN
   makes a column nullable when NULL/NOT NULL is omitted. Getting that
   wrong would quietly relax a constraint the legacy application relies
   on. */

/* An index that references a money column - as a key OR as an INCLUDE -
   blocks ALTER COLUMN outright: "The index ... is dependent on column ...".

   Rather than name the offending indexes, they are DISCOVERED. Naming them
   was the first approach and it broke twice: IX_student_DOJ_Branch was
   handled, then IX_monthlyFees_sturegid and IX_monthlyFees_Date1 were added
   to the coexistence script later and this script started failing again.
   Anything added in future would break it a third time.

   So: every non-clustered index touching a column about to be altered is
   scripted out, dropped, and recreated verbatim afterwards. */

IF OBJECT_ID('tempdb..#reindex') IS NOT NULL DROP TABLE #reindex;

CREATE TABLE #reindex (Seq int IDENTITY(1,1), TableName sysname, IndexName sysname,
                       CreateSql nvarchar(max));

INSERT INTO #reindex (TableName, IndexName, CreateSql)
SELECT t.name, i.name,
       N'CREATE ' + CASE WHEN i.is_unique = 1 THEN N'UNIQUE ' ELSE N'' END
     + N'INDEX ' + QUOTENAME(i.name) + N' ON dbo.' + QUOTENAME(t.name) + N' ('
     + STUFF((SELECT N', ' + QUOTENAME(c.name) + CASE WHEN ic.is_descending_key = 1 THEN N' DESC' ELSE N'' END
              FROM sys.index_columns ic
              JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                AND ic.is_included_column = 0
              ORDER BY ic.key_ordinal
              FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N'')
     + N')'
     + ISNULL(N' INCLUDE (' + STUFF((SELECT N', ' + QUOTENAME(c.name)
              FROM sys.index_columns ic
              JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                AND ic.is_included_column = 1
              ORDER BY ic.index_column_id
              FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N'') + N')', N'')
     + ISNULL(N' WHERE ' + i.filter_definition, N'') + N';'
FROM sys.indexes i
JOIN sys.tables t ON t.object_id = i.object_id
WHERE i.type_desc = 'NONCLUSTERED'
  AND i.is_primary_key = 0
  AND i.is_unique_constraint = 0
  AND EXISTS (
        SELECT 1
        FROM sys.index_columns ic
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        JOIN #money m ON m.TblName = t.name AND m.ColName = c.name
        WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id);

DECLARE @ix sysname, @ixTbl sysname, @dropSql nvarchar(max);

DECLARE ixcur CURSOR LOCAL FAST_FORWARD FOR SELECT TableName, IndexName FROM #reindex;
OPEN ixcur;
FETCH NEXT FROM ixcur INTO @ixTbl, @ix;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @dropSql = N'DROP INDEX ' + QUOTENAME(@ix) + N' ON dbo.' + QUOTENAME(@ixTbl) + N';';
    EXEC sp_executesql @dropSql;
    PRINT CONCAT('Dropped ', @ix, ' on ', @ixTbl, ' - recreated after the widening.');
    FETCH NEXT FROM ixcur INTO @ixTbl, @ix;
END

CLOSE ixcur; DEALLOCATE ixcur;
GO

DECLARE @tbl varchar(128), @col varchar(128), @sql nvarchar(max);
DECLARE @nullable bit, @scale int, @prec int, @changed int = 0, @skipped int = 0;

DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT m.TblName, m.ColName, c.is_nullable, c.scale, c.precision
    FROM #money m
    JOIN sys.columns c ON c.object_id = OBJECT_ID('dbo.' + m.TblName) AND c.name = m.ColName;

OPEN cur;
FETCH NEXT FROM cur INTO @tbl, @col, @nullable, @scale, @prec;

WHILE @@FETCH_STATUS = 0
BEGIN
    IF @scale >= 2
    BEGIN
        PRINT CONCAT('skip: ', @tbl, '.', @col, ' is already decimal(', @prec, ',', @scale, ').');
        SET @skipped = @skipped + 1;
    END
    ELSE
    BEGIN
        /* The column's OWN precision is carried through. Hard-coding a
           precision here would silently narrow any column that does not
           happen to match it. */
        SET @sql = N'ALTER TABLE dbo.[' + @tbl + N'] ALTER COLUMN [' + @col
                 + N'] decimal(' + CAST(@prec AS nvarchar(10)) + N',2) '
                 + CASE WHEN @nullable = 1 THEN N'NULL' ELSE N'NOT NULL' END;

        EXEC sp_executesql @sql;

        PRINT CONCAT('widened: ', @tbl, '.', @col, ' -> decimal(', @prec, ',2) ',
                     CASE WHEN @nullable = 1 THEN 'NULL' ELSE 'NOT NULL' END);
        SET @changed = @changed + 1;
    END

    FETCH NEXT FROM cur INTO @tbl, @col, @nullable, @scale, @prec;
END

CLOSE cur;
DEALLOCATE cur;

PRINT CONCAT('Widened ', @changed, ' column(s), skipped ', @skipped, ' already done.');
GO

/* Put back every index dropped above, from the definition captured before
   it was dropped - so an index comes back exactly as it was, including its
   INCLUDE list and any filter, rather than as somebody's recollection of it. */
DECLARE @seq int = 1, @maxSeq int, @rebuild nvarchar(max), @rName sysname, @rTable sysname;

SELECT @maxSeq = ISNULL(MAX(Seq), 0) FROM #reindex;

WHILE @seq <= @maxSeq
BEGIN
    SELECT @rebuild = CreateSql, @rName = IndexName, @rTable = TableName
    FROM #reindex WHERE Seq = @seq;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = @rName AND object_id = OBJECT_ID('dbo.' + @rTable))
    BEGIN
        EXEC sp_executesql @rebuild;
        PRINT CONCAT('Recreated ', @rName, ' on ', @rTable, '.');
    END

    SET @seq += 1;
END

DROP TABLE #reindex;
GO

/* ============================== 4. verification ===================== */

PRINT '';
PRINT '=====================================================';
PRINT 'VERIFICATION';
PRINT '=====================================================';
GO

SELECT TableName = m.TblName,
       ColumnName = m.ColName,
       NewType = CONCAT(ty.name, '(', c.precision, ',', c.scale, ')'),
       Status = CASE WHEN c.scale = 2 THEN 'OK' ELSE '*** STILL SCALE 0 ***' END
FROM #money m
JOIN sys.columns c ON c.object_id = OBJECT_ID('dbo.' + m.TblName) AND c.name = m.ColName
JOIN sys.types  ty ON ty.user_type_id = c.user_type_id
ORDER BY Status, m.TblName, m.ColName;
GO

PRINT '';
PRINT '--- LEGACY MUST STILL BE INTACT ---';
GO

SELECT Item = v.Item,
       Status = CASE WHEN v.Ok = 1 THEN 'OK' ELSE '*** LEGACY BROKEN ***' END
FROM (VALUES
    ('Users.Password still present',    CASE WHEN COL_LENGTH('dbo.Users','Password')      IS NOT NULL THEN 1 ELSE 0 END),
    ('Branch.Branch_name still present', CASE WHEN COL_LENGTH('dbo.Branch','Branch_name') IS NOT NULL THEN 1 ELSE 0 END),
    ('Expense column count still 4',
        CASE WHEN (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Expense')) = 4 THEN 1 ELSE 0 END),
    ('OIncome column count still 4',
        CASE WHEN (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OIncome')) = 4 THEN 1 ELSE 0 END),
    ('TecExpense column count still 4',
        CASE WHEN (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.TecExpense')) = 4 THEN 1 ELSE 0 END)
) AS v(Item, Ok)
ORDER BY v.Ok, v.Item;
GO

/* No amount should have been altered by the widening itself - adding
   decimal places to a whole number changes how it prints, not what it
   is worth. These totals are worth writing down and comparing with the
   figures the legacy reports showed before the run. */
PRINT '';
PRINT '--- totals AFTER (must match the BEFORE block above, rupee for rupee) ---';
GO

SELECT Measure = 'Expense total',     Value = (SELECT SUM(Amount)      FROM dbo.Expense)
UNION ALL SELECT 'OIncome total',              (SELECT SUM(IAmount)     FROM dbo.OIncome)
UNION ALL SELECT 'monthlyFees Installment',    (SELECT SUM(Installment) FROM dbo.monthlyFees)
UNION ALL SELECT 'student StuAdFees',          (SELECT SUM(StuAdFees)   FROM dbo.student)
UNION ALL SELECT 'TecFees TecSal',             (SELECT SUM(TecSal)      FROM dbo.TecFees);
GO

DROP TABLE #money;
GO

PRINT '';
PRINT CONCAT('Finished : ', CONVERT(varchar(19), SYSDATETIME(), 120));
PRINT 'MONEY_PRECISION_UPGRADE.sql complete.';
GO
