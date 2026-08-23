/* =====================================================================
   008_migrate_finance_data.sql
   Source    : ph19987796653_   (legacy, READ ONLY)
   Target    : Avijan_Angular_Migration
   Purpose   : Load the real fee ledger, expenses, fines, teacher payouts,
               marks, branches and notices.

   This REPLACES the sample rows that 002 inserted. Until this runs, the
   dashboard reports seeded figures rather than the institute's own.

   Transformations
   ---------------
   F1  Fake seed rows (CreatedBy = 'seed') are deleted from MonthlyFees,
       Expense, OIncome and Notice before the real load.
   F2  Identities preserved (IDENTITY_INSERT) -- BillNo and StuRegId tie
       these tables together and to Student.
   F3  Extra placeholder Teacher rows for ids referenced by TecFees but
       already deleted from Teacher (ids 19 and 22 in addition to the eight
       004 created for Student). Same reasoning as 004 T4: keep the payout
       history attributable rather than nulling it.
   F4  TecFees.tecId = 0 -> NULL (legacy "unassigned" sentinel).
   F5  datetime -> date for DOD / UpdateDate / InDate / ExDate / StoreDate /
       Date1 / PDate / ExDate(Marks); decimal precision made explicit.
   F6  Notice.NoticeDate kept as datetime2 -- the board shows a timestamp.

   Idempotent: re-running clears the migrated rows and reloads them.
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE [Avijan_Angular_Migration];
GO

IF DB_ID('ph19987796653_') IS NULL
BEGIN
    RAISERROR('Source database ph19987796653_ is not attached on this server.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* --------------------------------------- F3: placeholders for TecFees ids */
SET IDENTITY_INSERT dbo.Teacher ON;

INSERT INTO dbo.Teacher (id, TecName, LoginId, TecPhone, CreatedBy, CreatedOn)
SELECT DISTINCT t.tecId,
       '(removed teacher #' + CAST(t.tecId AS varchar(10)) + ')',
       '__removed_' + CAST(t.tecId AS varchar(10)),
       NULL,
       'migration-004-orphan',
       SYSDATETIME()
FROM   ph19987796653_prod.dbo.TecFees t
WHERE  t.tecId > 0
  AND  NOT EXISTS (SELECT 1 FROM dbo.Teacher x WHERE x.id = t.tecId);

SET IDENTITY_INSERT dbo.Teacher OFF;
GO

/* ------------------------------------------------- F1: clear seeded rows */
DELETE FROM dbo.MonthlyFees;
DELETE FROM dbo.Expense;
DELETE FROM dbo.OIncome;
DELETE FROM dbo.Notice;
DELETE FROM dbo.LateFees;
DELETE FROM dbo.TecFees;
DELETE FROM dbo.TecExpense;
DELETE FROM dbo.Marks;
DELETE FROM dbo.Branch;
DELETE FROM dbo.Fees;
GO

/* ---------------------------------------------------------- MonthlyFees */
SET IDENTITY_INSERT dbo.MonthlyFees ON;

INSERT INTO dbo.MonthlyFees
    (id, StuRegId, BillNo, Date1, PDate, Installment, OInstallment, percen, OldDue, NewDue, CreatedBy, CreatedOn)
SELECT  m.id,
        LTRIM(RTRIM(m.sturegid)),
        LTRIM(RTRIM(m.BillNo)),
        TRY_CONVERT(date, m.Date1),
        TRY_CONVERT(date, m.PDate),
        m.Installment,
        m.OInstallment,
        m.percen,
        m.OldDue,
        m.NewDue,
        'migration-008',
        SYSDATETIME()
FROM    ph19987796653_prod.dbo.monthlyFees AS m;

SET IDENTITY_INSERT dbo.MonthlyFees OFF;
GO

/* -------------------------------------------------------------- Expense */
SET IDENTITY_INSERT dbo.Expense ON;

INSERT INTO dbo.Expense (id, ExDetails, ExDate, Amount, CreatedBy, CreatedOn)
SELECT  e.id, e.ExDetails, TRY_CONVERT(date, e.ExDate), e.Amount, 'migration-008', SYSDATETIME()
FROM    ph19987796653_prod.dbo.Expense AS e;

SET IDENTITY_INSERT dbo.Expense OFF;
GO

/* -------------------------------------------------------------- OIncome */
SET IDENTITY_INSERT dbo.OIncome ON;

INSERT INTO dbo.OIncome (ID, IPurpose, IAmount, ODate, CreatedBy, CreatedOn)
SELECT  o.ID, o.IPurpose, o.IAmount, TRY_CONVERT(date, o.ODate), 'migration-008', SYSDATETIME()
FROM    ph19987796653_prod.dbo.OIncome AS o;

SET IDENTITY_INSERT dbo.OIncome OFF;
GO

/* --------------------------------------------------------------- Notice */
SET IDENTITY_INSERT dbo.Notice ON;

INSERT INTO dbo.Notice (NID, NewNotice, NoticeDate, CreatedBy, CreatedOn)
SELECT  n.NID, n.NewNotice, TRY_CONVERT(datetime2(0), n.NoticeDate), 'migration-008', SYSDATETIME()
FROM    ph19987796653_prod.dbo.Notice AS n;

SET IDENTITY_INSERT dbo.Notice OFF;
GO

/* ------------------------------------------------------------- LateFees */
SET IDENTITY_INSERT dbo.LateFees ON;

INSERT INTO dbo.LateFees (id, StuRegId, BillNo, FineFees, DOD, CreatedBy, CreatedOn)
SELECT  l.id,
        LTRIM(RTRIM(l.StuRegid)),
        LTRIM(RTRIM(l.BillNo)),
        l.FineFees,
        TRY_CONVERT(date, l.DOD),
        'migration-008',
        SYSDATETIME()
FROM    ph19987796653_prod.dbo.LateFees AS l;

SET IDENTITY_INSERT dbo.LateFees OFF;
GO

/* -------------------------------------------------------------- TecFees */
SET IDENTITY_INSERT dbo.TecFees ON;

INSERT INTO dbo.TecFees (id, TecId, StuRegId, BillNo, TecSal, UpdateDate, CreatedBy, CreatedOn)
SELECT  t.id,
        NULLIF(t.tecId, 0),                      -- F4
        LTRIM(RTRIM(t.StuRegId)),
        LTRIM(RTRIM(t.BillNo)),
        t.TecSal,
        TRY_CONVERT(date, t.UpdateDate),
        'migration-008',
        SYSDATETIME()
FROM    ph19987796653_prod.dbo.TecFees AS t;

SET IDENTITY_INSERT dbo.TecFees OFF;
GO

/* ----------------------------------------------------------- TecExpense */
SET IDENTITY_INSERT dbo.TecExpense ON;

INSERT INTO dbo.TecExpense (id, TecId, InDate, Amount, CreatedBy, CreatedOn)
SELECT  x.id, NULLIF(x.tecid, 0), TRY_CONVERT(date, x.InDate), x.Amount, 'migration-008', SYSDATETIME()
FROM    ph19987796653_prod.dbo.TecExpense AS x;

SET IDENTITY_INSERT dbo.TecExpense OFF;
GO

/* ---------------------------------------------------------------- Marks */
SET IDENTITY_INSERT dbo.Marks ON;

INSERT INTO dbo.Marks
    (id, RegId, ExDate, Sub1, M1, OM1, Sub2, M2, OM2, Sub3, M3, OM3, Sub4, M4, OM4, CreatedBy, CreatedOn)
SELECT  k.id,
        LTRIM(RTRIM(k.Regid)),
        TRY_CONVERT(date, k.ExDate),
        k.Sub1, k.M1, k.OM1,
        k.Sub2, k.M2, k.OM2,
        k.Sub3, k.M3, k.OM3,
        k.Sub4, k.M4, k.OM4,
        'migration-008',
        SYSDATETIME()
FROM    ph19987796653_prod.dbo.Marks AS k;

SET IDENTITY_INSERT dbo.Marks OFF;
GO

/* --------------------------------------------------------------- Branch */
SET IDENTITY_INSERT dbo.Branch ON;

INSERT INTO dbo.Branch (id, BranchName, BranchCode, IsActive, CreatedBy, CreatedOn)
SELECT  b.id, b.Branch_name, b.Branch_id, ISNULL(b.Is_active, 1), 'migration-008', SYSDATETIME()
FROM    ph19987796653_prod.dbo.Branch AS b;

SET IDENTITY_INSERT dbo.Branch OFF;
GO

/* ----------------------------------------------------------------- Fees */
SET IDENTITY_INSERT dbo.Fees ON;

INSERT INTO dbo.Fees (id, StuReg, Total, StoreDate, CreatedBy, CreatedOn)
SELECT  f.id, LTRIM(RTRIM(f.StuReg)), f.Total, TRY_CONVERT(date, f.StoreDate), 'migration-008', SYSDATETIME()
FROM    ph19987796653_prod.dbo.Fees AS f;

SET IDENTITY_INSERT dbo.Fees OFF;
GO

COMMIT TRANSACTION;
GO

DBCC CHECKIDENT ('dbo.MonthlyFees', RESEED) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Expense',     RESEED) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.OIncome',     RESEED) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Notice',      RESEED) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.LateFees',    RESEED) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.TecFees',     RESEED) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.TecExpense',  RESEED) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Marks',       RESEED) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Branch',      RESEED) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Fees',        RESEED) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Teacher',     RESEED) WITH NO_INFOMSGS;
GO

PRINT '008_migrate_finance_data.sql applied.';
GO
