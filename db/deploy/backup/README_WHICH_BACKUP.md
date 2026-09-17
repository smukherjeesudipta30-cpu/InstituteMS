# Restoring the database on your own machine

> `avijan_sql2019.zip` was refreshed on **12 September 2026** from the live server and
> re-tested by restoring it into an empty database: **48 tables, 0 errors**.

## Why there is a `.sql` at all, not a `.bak`

**A SQL Server backup only restores forward.** The live server runs SQL Server
2025, which stamps a database at internal version 998. SQL Server 2019 reads up to
904 and refuses the file outright:

```
The database was backed up on a server running version 17.00.1000.
That version is incompatible with this server, which is running version 15.00.xxxx.
```

Nothing can be done to a `.bak` to change that — not a compatibility-level setting,
not a restore option. Downgrading a database between major versions is not
supported at all, so the data has to leave as *script* rather than as pages.

`avijan_sql2019.zip` is that script: every table, index, key, default and row,
generated with SMO targeting SQL Server 2019 so the syntax is one that 2019
understands. It restores on SQL Server 2019 or anything newer.

## Restoring it

Unzip it first — it is about 19 MB of SQL inside a 4.5 MB zip.

**In SSMS:** create an empty database called `Avijan`, open `avijan_sql2019.sql`
against it, and execute. It takes well under a minute.

**Or from the command line:**

```powershell
sqlcmd -S .\SQLEXPRESS -E -C -b -Q "CREATE DATABASE Avijan;"
sqlcmd -S .\SQLEXPRESS -E -C -b -d Avijan -i avijan_sql2019.sql
```

`-C` is required with ODBC Driver 18 — without it the connection is refused because
SQL Express presents a self-signed certificate. `-b` makes it stop on the first
error instead of carrying on.

The script creates tables into whatever database you are connected to; it does
**not** create the database itself. That is deliberate — it lets you call it
something else if you want.

### It was tested, not assumed

This exact file was run into an empty database on the day it was produced: **47
tables, 0 errors.** Identity values are preserved throughout, which matters — the
application's registration numbers depend on them.

## What is in it

| | |
|---|---|
| Tables | 47 |
| Students | 957 |
| Monthly fee rows | 8,741 |
| Teacher payouts | 11,833 |
| Staff attendance scans | 83 |
| **Plain-text passwords remaining** | **0** |

Includes everything through 8 September 2026 — the dynamic staff hierarchy (with
the duplicate teacher/receptionist boxes on the chart now linked to their real
records instead of double-counted, and a "Link to staff record" picker on the
Hierarchy screen's own edit form so the office can fix the next one themselves in
one click, without a database change), the competition portal (registration,
payments, chest numbers, adverts, bilingual rules), cash/bank payment-mode
tracking on every fee and admission, the Bengali phonetic editor (runs entirely in
the browser — nothing to migrate), the attendance session-gap fix, receptionist
access to the expense register (entry and report, matching the access they
already had to fees), and deactivating a student now disabling their portal
login along with it (previously only the student record was flagged inactive —
the sign-in itself stayed live indefinitely), and a save-and-resume step in front
of the competition registration form — a visitor gets a registration id and sets
their own password, can leave and come back later to finish, and Admin/Editor see
a dashboard alert the moment one is started so an abandoned one can be followed up
by phone. Starting one reserves no dance-group seat; only a completed, submitted
registration does.

**Every legacy duplicate registration id is now fixed** — 15 numbers that had
been issued to more than one student (32 rows total, all pre-dating this system)
each now have their own permanent number, drawn from the real numbering
sequence so nothing can collide again. Nothing was deleted and no bill number
changed; **957 students, 957 distinct registration ids** — verified, not assumed.

**A missed-scan-out deduction is available on the Teacher Account report**: 3–5
missed scan-outs in a calendar month costs the teacher 5% of that month's
earnings, 6–8 costs 10%, 9–11 costs 15%, and so on — another 5% for every
further block of 3 — evaluated one month at a time even across a multi-month
report. The reason is spelled out in plain language and shown
identically to the teacher looking at their own account and to the office
looking at theirs. It changes what is reported as owed, not the underlying
payout ledger. **Off by default** — an Admin-only switch on the report screen
turns it on or off for everyone, immediately, with no redeploy; `dbo.PayrollSettings`
holds that one setting.

**An admission fee can now be part paid.** It used to be all-or-nothing: the
amount entered was taken as received in full. A student paying 300 of a 600
admission fee now leaves 300 owing, collectable later from either the student's
own row or the fees counter, in Cash or Bank, with a receipt per instalment
(`dbo.AdmissionFeePayment`) and a running total on the student
(`AdmissionFeesPaid`). The balance shows on the Outstanding Dues report as its
own figure — **not** added into the monthly arrears, and not bounded by the
report's date range, since a student up to date on monthly fees can still owe
it. Admission money stays in the admission bucket: an instalment writes no
monthly-fee row and earns no teacher percentage. See
`db/migrations/051_admission_fee_part_payment.sql` — whose backfill marks all
960 existing students paid in full, because that is what those records mean.

**Editing a student no longer erases their personal email.** The edit form
never loaded the personal email (the row it read from did not carry it) and so
posted it back empty on every save, wiping it silently — and that address is
where fee receipts and password-reset codes go. Two real students had already
lost theirs (SWAGATA ROY, SUBHAM HALDER); both were restored from the address the
admission welcome email was actually delivered to. Fixed at three layers: the row
now carries the field, the form loads it, and the server treats a request that
does not send the field as "keep what is there" rather than "clear it" — a
deliberate clear is an empty string.

**A wrongly recorded admission amount can be corrected.** If 250 was typed at
admission when 750 was actually taken, the office edits the student: the
admission fee to 750 and "Admission fee received" to 750, and nothing is left
reading as owing. The receipt written on the admission day is corrected to
match (amount and Cash/Bank mode), and carries a note of the old figure and who
changed it. Received can never exceed the fee. This is a correction of a past
figure; taking NEW money against a balance still goes through Collect, which
records each instalment with its own date and Cash/Bank mode — viewable at any
time from "View receipts" on the edit form, and counted on Fee Collections
under the mode it was actually taken in.

**Admission money is now findable on the Fee Collections report.** It was
always there, but a month has a hundred-odd monthly rows and a handful of
admissions, and in a date-sorted list paged at 25 the admissions landed on page
four where nobody reconciling the drawer saw them. The report now has a
"Payment kind" filter (monthly / admission / both), a Monthly-fees and an
Admission-fees tile that always add up to the grand total whatever the filter,
and admission rows carry a coloured tag in the list. The Students edit form
also shows what has been received against the admission fee and what is still
owing, read-only, with a Collect button when there is a balance.

**The Fee Collections report reads admission money from the receipts.** It
already listed admission fees, but took the whole `StuAdFees` off the student row
dated at joining — right while the fee was all-or-nothing, wrong in three ways
once it could be part paid: it reported the full fee as collected on the joining
day when only part had been taken, it never saw a later instalment, and it
stamped every rupee with the mode of the first payment, so a bank transfer
settling a cash admission counted as cash. Since this report is the month-end
drawer reconciliation, that overstatement is the exact failure it exists to
catch. Instalments now appear on their own date under their own mode, labelled
"Admission fee instalment". Admissions predating migration 051 have no receipts
and are still read from the student row, so every past month reconciles to the
same figure as before.

**The staff hierarchy no longer draws a person twice.** Deduplication only
matched on the staff *link*, so a box typed in by hand with no link was invisible
to it and the person appeared twice — once by hand, once from the staff register.
It now also matches on name, with the hand-made box winning and the automatic one
stepping aside. Where the two spellings differed the box had to be linked
properly instead; that was done for Chumki Haldar, whose teacher record was
corrected to the same spelling.

**The backup script no longer overshoots the registration sequence.** It added
the increment to `CurrentValue` blindly, which is right in the ordinary case but
one too many straight after an `ALTER SEQUENCE ... RESTART` — so a restored copy
skipped a number that was never issued. It now decides the same way the
application's own preview does: if the current value is already on a student the
next one is free, otherwise it is itself the next one.

**Fixed a real gap in "forgot password" for staff**: a Teacher or Receptionist
signs in with whatever login was set up for them, which is not always the
address they actually read — both roles carry their own `PersonalEmail`,
separate from `LoginId`, for exactly this reason (a Teacher record can have
`LoginId=san@avijan.in` but `PersonalEmail=nitadhar286@gmail.com`, say). The
reset code now goes to that personal address for every role that has one on
file — Student already did, Teacher and Receptionist now do too — falling
back to the sign-in id only when no personal address exists. No schema
change, `AuthService.ForgotPasswordAsync` only.

**"Forgot password" now covers the competition portal too**, not just
staff/student sign-in — a competition draft never had an email on file at
all, only a phone number, so registering now asks for one (required, since
it's the only place a reset code has anywhere to go). Same shape as the main
system: `POST /drafts/forgot-password` emails the code, `POST
/drafts/reset-password` spends it. A draft started before this asks for the
office's help by phone instead — its dashboard row already carries the
password in clear, same as it always has. `dbo.CompetitionDraft` gained the
matching four columns — see `db/migrations/050_competition_draft_forgot_password.sql`.

**"Forgot password" now exists** — until today there was no way back into an
account for someone who could not sign in at all; only a self-service change
for someone already signed in. `/forgot-password` emails a six-digit code
(valid 15 minutes, single use, five wrong tries locks it), and `/reset-password`
spends it to set a new password with no old one needed. A student's code goes
to the personal email on file, the same address admission mail already used —
not their portal sign-in address, which for a student is often not an inbox
they otherwise open. Staff already sign in with their real address, so theirs
needs no such redirection. Neither response ever says whether an address
matched an account. `dbo.Users` gained three nullable columns for this
(`PasswordResetCodeHash`, `PasswordResetCodeExpiry`, `PasswordResetAttempts`) —
see `db/migrations/049_password_reset_code.sql`.

**Self-service password reset is now open to every role**, not just students —
Teacher, Receptionist, Editor, and Admin can all change their own password any
time from the account menu (no longer only on a forced first-login reset), and
a competition registrant can do the same for their draft registration. Every
one of these now raises the same office notification a student's password
change always has, visible in the same dashboard grid.

**A Practical exam date now updates a student's admit card automatically**,
and only Admin or Receptionist can set it — and only once that specific
student has actually submitted the Theory paper for the same course and term;
enforced on the server, not just hidden in the screen. The admit card itself
no longer asks for a signature: it reads "This is a system-generated admit
card. No signature is required." in place of the old signature lines, since
nothing about it is filled in by hand any more.

**A new-admission crash for long staff logins is fixed** — `dbo.student.loginId`
and `dbo.Users.Loginid` were `varchar(20)`; every new admission stamps the
creating staff member's own login into that field, so any receptionist or admin
whose login exceeds 20 characters (most email logins do) got a hard SQL
truncation error and "something went wrong" on every single admission attempt.
Both columns are now `varchar(100)`. Confirmed against the real, live-affected
receptionist login before this was called fixed, not just reasoned about.

**Sign-in for `admin@avijan.local` changed on 4 September 2026** — see the current
password directly with the owner rather than assuming an old one still works.

## Taking a fresh snapshot later

**The live system is in use** — students are being admitted and enquiries taken, so
any file here is a point-in-time copy and goes stale immediately. To take a new
one, on the server:

```powershell
powershell -ExecutionPolicy Bypass -File export-for-sql2019.ps1 -Out C:\Deploy\backup\avijan_sql2019.sql
```

It needs the `SqlServer` PowerShell module (`Install-Module SqlServer`). On Windows
PowerShell 5.1 you must enable TLS 1.2 first or the gallery download fails:

```powershell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
```

## After restoring, to actually run the application

The database alone is not enough. You also need:

1. **A connection string** pointing at your own server:

   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Server=.\\SQLEXPRESS;Database=Avijan;Integrated Security=SSPI;TrustServerCertificate=True;MultipleActiveResultSets=True"
   }
   ```

2. **A `Jwt:Key`** of at least 32 characters — the application refuses to start
   without one. Any random string will do locally.

3. **Leave `Mail:Enabled` as `false`** locally, or a test registration will email a
   real student.

For development this goes in
`Avijan_Migration_API/src/InstituteMS.Api/appsettings.Development.json`. See
`DEPLOYMENT.md` at the repository root for the production arrangement.

## Signing in

`admin@avijan.local` — the current password is the one set on the live server on
4 September 2026; ask the owner for it rather than relying on anything documented
here previously. If you do not have it, clear the hash and let the seeder set one
from configuration:

```sql
UPDATE dbo.Users SET PasswordHash = NULL WHERE Email = 'admin@avijan.local';
```

Set `Seed:AdminPassword` to whatever you want, then start the application; it
hashes it on startup.

Migrated accounts sign in with the password they already had and are then made to
choose a new one on first login.
