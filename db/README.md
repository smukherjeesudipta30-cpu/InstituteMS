# Database scripts

Target database: **`Avijan_Angular_Migration`** on `.` (Windows authentication).
Legacy source database: **`ph19987796653_`** — read only, never written to.

Every script is idempotent, so the whole set can be re-run from scratch.

## Run order

```bash
sqlcmd -S . -E -d Avijan_Angular_Migration -I -b -i db/migrations/001_create_core_schema.sql
```

```bash
sqlcmd -S . -E -d Avijan_Angular_Migration -I -b -i db/migrations/002_seed_reference_data.sql
```

```bash
sqlcmd -S . -E -d Avijan_Angular_Migration -I -b -i db/migrations/003_student_module_schema.sql
```

```bash
sqlcmd -S . -E -d Avijan_Angular_Migration -I -b -i db/migrations/004_migrate_student_data.sql
```

```bash
powershell -ExecutionPolicy Bypass -File db/migrations/005_import_local_photo_files.ps1
```

```bash
sqlcmd -S . -E -d Avijan_Angular_Migration -I -b -i db/migrations/006_imageupload_storage_flag.sql
```

```bash
sqlcmd -S . -E -d Avijan_Angular_Migration -I -b -i db/migrations/007_finance_academic_schema.sql
```

```bash
sqlcmd -S . -E -d Avijan_Angular_Migration -I -b -i db/migrations/008_migrate_finance_data.sql
```

```bash
sqlcmd -S . -E -d Avijan_Angular_Migration -I -b -i db/migrations/009_registration_number_sequence.sql
```

```bash
sqlcmd -S . -E -d Avijan_Angular_Migration -I -b -i db/migrations/010_monthlyfees_actual_fee.sql
```

Users last, because it hashes passwords and so cannot be a `.sql` file:

```bash
dotnet run --project Avijan_Migration_API/src/InstituteMS.Tools.DataMigration
```

Then check the result:

```bash
sqlcmd -S . -E -d Avijan_Angular_Migration -I -W -i db/verify/compare_students.sql
```

```bash
sqlcmd -S . -E -d Avijan_Angular_Migration -I -W -i db/verify/compare_finance.sql
```

> `-I` matters: sqlcmd defaults `QUOTED_IDENTIFIER` to OFF, which makes filtered
> index creation fail.

## What each script does

| Script | Purpose |
|---|---|
| `001_create_core_schema.sql` | Core tables (Course, Teacher, Student, MonthlyFees, Expense, OIncome, Notice, Users, CusMessage) with real keys, `date`/`decimal` types and the Users security columns. |
| `002_seed_reference_data.sql` | Sample rows so the dashboard renders before real data lands. **`004` deletes all of these** — it only matters if you run `001`+`002` alone. |
| `003_student_module_schema.sql` | Student-module schema: extra legacy columns, `ImageUpload`, search indexes, and relaxing the `StuRegId` unique index (see below). |
| `004_migrate_student_data.sql` | Loads the real Course / Teacher / Student / ImageUpload data from `ph19987796653_`, preserving identities. |
| `005_import_local_photo_files.ps1` | Recovers photos that the old uploader wrote to disk, from the backup's `UploadedFiles` folder. PowerShell because it reads binary files. |
| `006_imageupload_storage_flag.sql` | Adds `ImageUpload.IsInline` so readers know whether `FilePath` is a Base64 payload or a disk path. |
| `007_finance_academic_schema.sql` | LateFees, TecFees, TecExpense, Marks, Branch, Fees; relaxes the `Users.Email` unique index. |
| `008_migrate_finance_data.sql` | Loads the real fee ledger (3,397), expenses (314), fines (3,396), teacher payouts (4,113), marks, branches and notices. **Deletes the `002` sample rows.** |
| `009_registration_number_sequence.sql` | Creates `dbo.StudentRegistrationNumber`, seeded above the highest number already in use, so new registration numbers stop depending on `Student.id`. |
| `InstituteMS.Tools.DataMigration` | Migrates the 302 users, BCrypt-hashing each legacy password so existing logins keep working. A console tool because SQL Server cannot compute BCrypt. |
| `verify/compare_students.sql` | Old-vs-new diff for students: row counts, per-field checksums, FK integrity. Read only. |
| `010_monthlyfees_actual_fee.sql` | Restores `MonthlyFees.stu_actual_monthly_fees`, which 008 missed — the agreed fee at the time of each payment. |
| `verify/compare_finance.sql` | Old-vs-new diff for users and money: totals to the paisa, hash checks, role breakdown. Read only. |
| `verify/compare_income.sql` | Runs the original income SQL beside the new definitions, shows the `UNION` shortfall, and checks the late-fine containment rule. Read only. |
| `verify/compare_academics.sql` | Old-vs-new diff for courses, marks and notices: row-for-row on the key, all four mark triples, orphan and `obtained > full` checks. Read only. |
| `011_stage_production_snapshot.sql` | Mirrors the LIVE production database into `ph19987796653_prod` over a read-only linked server. |
| `012_production_schema_alignment.sql` | Widens `Teacher.LoginId`, adds `Student.Avijan_login_Password`, creates the four tables production has that this schema did not. |
| `013_reset_target_for_reload.sql` | Empties the application database in FK order so a reload can run cleanly. |
| `014_migrate_unmodelled_tables.sql` | Loads attendance, daily routine, the mail log, `Login` and the contact messages no earlier script carried. |
| `015_purge_non_production_rows.sql` | Removes anything the running application created, leaving only migrated data. Keeps the FK placeholders. |
| `verify/compare_production_snapshot.sql` | Proves the local mirror matches live production row for row. Read only. |
| `verify/compare_production_to_target.sql` | Proves the application database matches live production, with the placeholder and admin differences stated. Read only. |

## Reloading from live production

The local database is a copy of the **live** production database, not the older
restored backup. The backup was roughly half the size — 459 students against 953 —
so anything tested against it was testing against stale data.

Production is only ever read. The linked server is created with `rpc out = false`,
so no remote procedure can run through it; every statement against it is a `SELECT`.

Run in this order:

```
011_stage_production_snapshot.sql    -- mirror production into ph19987796653_prod
012_production_schema_alignment.sql  -- widen/add columns production needs
013_reset_target_for_reload.sql      -- empty the application database
004_migrate_student_data.sql         -- courses, teachers, students, photos
008_migrate_finance_data.sql         -- fees, fines, payouts, marks, notices
010_monthlyfees_actual_fee.sql       -- agreed fee at time of payment
014_migrate_unmodelled_tables.sql    -- attendance, routine, mail log, messages
009_registration_number_sequence.sql -- reseed the number sequence
InstituteMS.Tools.DataMigration --source ph19987796653_prod
015_purge_non_production_rows.sql    -- drop anything the app created while testing
```

Then verify:

```
verify\compare_production_snapshot.sql   -- mirror matches production
verify\compare_production_to_target.sql  -- application database matches production
```

### What production forced us to change

**`Teacher.LoginId` was too narrow.** Production declares `varchar(50)` and holds
values up to 27 characters; this schema had `varchar(20)`, copied from the older
backup. The load would have failed outright. Widened here, and in
`TeacherConfiguration` and `TeacherSaveRequestValidator` to match.

**Four tables live in the `ph19987796653` schema on production, not `dbo`** —
`dailyRoutine`, `MailLog_registration_time`, **`OIncome`** and **`TecExpense`**. The
old backup had all of them in `dbo`. Any script assuming `dbo` silently misses other
income and teacher expenses, two things the income report depends on. The snapshot
flattens them into `dbo` so downstream scripts have one place to look.

**`CusMessage` had never been migrated at all.** The table is modelled by the
application but no script loaded it, so all 15 contact-form messages were being
dropped. Caught by `compare_production_to_target.sql`; loaded by 014.

**Orphaned references are wider than we knew.** Production has 10 students on a course
that no longer exists, and 20 teacher ids referenced by students, `TecFees` or
`TecExpense` with no matching `Teacher` row — 6 of which appear on no student at all,
so the earlier student-only placeholder logic missed them. Placeholders are created
for both, named `(removed course #n)` / `(removed teacher #n)`, so no reference is
silently nulled.

**`Student.Avijan_login_Password` was missing from this schema.** 30 students have a
portal password on production. It is now carried across verbatim so the copy is
complete — **in clear text, as production stores it**. Nothing in the application
reads it; student sign-in goes through `Users.PasswordHash`. Hash or drop it before
this schema is used for anything but testing.

### Not migrated, on purpose

`Branch` is empty on production and the new application has its own branch master.

`AttendanceMaster` (344), `dailyRoutine` (2), `MailLog_registration_time` (26) and
`Login` (0) have no migrated module yet. They are copied so the data is present when
a module is built for them.

### Keeping the database pure

Run 015 whenever testing has left rows behind. It identifies them by their
audit columns - anything whose CreatedBy is not 'migration-*' was created
through the running application - so it can be run repeatedly and never
touches migrated data.

Placeholder rows are NOT removed by it. Production has 10 students on a course
that no longer exists and 20 teacher ids with no Teacher row; their
'(removed course #n)' / '(removed teacher #n)' stand-ins are what let those real
rows keep their real references. Deleting them would break the foreign keys and
lose which course or teacher a student actually had.

The one row production does not have is **admin@avijan.local**, created by the
migration tool so there is always a way in. Every production Admin still works
with the password its owner already had, so once you can sign in as one of those:

```sql
DELETE FROM dbo.Users WHERE Email = 'admin@avijan.local';
```

### The linked server holds production credentials

`AVIJAN_PROD` stores the production password inside the local SQL Server. Drop it when
the sync is done — the verify scripts need it, so keep it while testing:

```sql
EXEC sp_dropserver 'AVIJAN_PROD', 'droplogins';
```

## Decisions the real data forced

**`StuRegId` is not unique.** The plan assumed it could be. The live data has
**12 registration ids shared by 2–3 students**, caused by the unlocked
`SELECT TOP 1 id … ORDER BY id DESC` + 1 generator in `Models/Student.cs:108-146`.
Several of those pairs are genuinely different people (`AVI/139/20` is both Pradip
Pramanik and Diptadip Ghosh). Renumbering would rewrite a business key that
`MonthlyFees`, `Marks`, `TecFees` and `ImageUpload` all join on, so the data is
migrated verbatim and the index is non-unique. New registrations are still unique —
`RegistrationNumberService` takes an `UPDLOCK, HOLDLOCK` inside the insert
transaction.

To review them:

```bash
sqlcmd -S . -E -d Avijan_Angular_Migration -I -W -Q "SELECT StuRegId, COUNT(*) AS Students FROM Student GROUP BY StuRegId HAVING COUNT(*) > 1 ORDER BY StuRegId"
```

**Sentinel zeros became NULL.** `Cid = 0` (27 students) and `TecName/1/2 = 0`
(1 / 361 / 458) were "nothing selected" markers with no matching parent row.

**Deleted teachers were recreated as placeholders.** The legacy app deletes
teachers with no referential guard, orphaning ids 6, 8, 9, 25, 27, 28, 30 and 32
across **45 students**. Rather than discard who taught them, `004` creates
`(removed teacher #n)` rows with login `__removed_n`. They sort last in dropdowns
and are flagged `isPlaceholder` in the API.

```bash
sqlcmd -S . -E -d Avijan_Angular_Migration -I -W -Q "SELECT id, TecName FROM Teacher WHERE CreatedBy = 'migration-004-orphan' ORDER BY id"
```

**Plain-text passwords were not copied.** `Student.Avijan_login_Password` exists in
the legacy table; it is deliberately absent here. The 302 `Users` rows were migrated
with each legacy password **hashed with BCrypt**, so every existing login still
works while no plain text reaches the new database. There is no `Users.Password`
column at all.

**Four email addresses are shared by two accounts each.** All are migrated; login
resolves to the most recently registered active account and logs a warning. Listed
at the end of `verify/compare_finance.sql`.

**Registration numbers now come from a sequence, not from `Student.id`.** The
legacy generator derived the number from the last student id, which the live data
shows drifting: a row landed on id 3477 while the number issued was 3463, because
`IDENTITY` burns values on rolled-back inserts. Worse, once the branch segment
appeared, `AVI/3463/26` and `AVI/Bar/3463/26` were different strings but the same
number, so it could be handed out twice. `009` gives the number its own sequence and
the service checks the number rather than the formatted string.

**Branch codes come from `dbo.Branch`.** Rajpur → `Raj`, Baruipur → `Bar`, producing
`AVI/{code}/{n}/{yy}`. The live system had a bug here — student 3461 is Baruipur but
was issued `AVI/Raj/3461/26` — so the code is now read from the student's own branch
row. Leaving the branch blank gives the older `AVI/{n}/{yy}` that 448 of the 459
migrated students use.

**Late fines are inside the fee figure, not on top of it.**
`MonthlyFees.Installment` is stored as *installment + fine*, so any report that adds
`LateFees` to it counts the fine twice. Checked across the data: of 1,439 rows with a
fine, all 1,439 have `fine <= Installment`. The income report and the dashboard both
treat fines as an "of which" breakdown.

**Roles are wider than the plan assumed.** The live data holds `Receptionist` and
the combined `Receptionist & Teacher` alongside the four planned roles, plus one
`sudent` typo that the user migration corrects. A user can hold more than one role,
so the JWT carries one role claim per role.

**97 photos are still only on the production web server.** `ImageUpload` mixes two
uploader generations: 7 rows hold Base64 (servable), 94 hold a path like
`../UploadedFiles/<guid>.jpg` whose bytes were never in the database. `005`
recovered the 4 whose files exist in the backup. To recover the rest, copy the
production `UploadedFiles` folder to this machine and re-run:

```bash
powershell -ExecutionPolicy Bypass -File db/migrations/005_import_local_photo_files.ps1 -UploadRoot "<path-to-production-UploadedFiles>"
```

then re-run `006` to refresh the flags.
