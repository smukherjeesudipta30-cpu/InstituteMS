# API contract — delivered slice

> Covers the three pages built so far: **About Us**, **Admin Login**, **Home Dashboard**.
> Full target surface is in [`MIGRATION_PLAN.md`](../../backup_TestAngularProject%20-%2031052026/MIGRATION_PLAN.md) §5.
> Live spec: `http://localhost:5074/swagger/v1/swagger.json` (Development only).
> Implementation: `Avijan_Migration_API/src/InstituteMS.Api/Controllers/v1/`.

Base URL: `http://localhost:5074/api/v1`

## Response envelope

Every endpoint — success or failure — returns the same shape (MIGRATION_PLAN §3.1):

```json
{
  "success": true,
  "data": { },
  "message": "Signed in.",
  "errors": [],
  "traceId": "00-4c83cfeb115636a2c1d1c25c1c20aca2-74bc8c4877829861-00"
}
```

`traceId` matches the Serilog entry for the request, so a client crash report maps
straight onto a server log line.

## Endpoints

| Method | Route | Auth | Replaces |
|---|---|---|---|
| POST | `/auth/login` | anonymous, rate-limited | `AccountController.Login` |
| POST | `/auth/refresh` | anonymous, rate-limited | new |
| POST | `/auth/logout` | any role | `AccountController.LogOff` |
| GET | `/auth/me` | any role | `Session["UserID"]` |
| POST | `/auth/change-password` | any role | `ManageController.ChangePassword` |
| GET | `/dashboard/summary` | `Admin`, `Editor` | `Views/About/Index.cshtml` dashboard |
| GET | `/students` | `Admin`, `Editor` | `getDetails`, `getInactiveDetails`, `SearchData`, `SearchData1` |
| GET | `/students/{id}` | `Admin`, `Editor` | — |
| GET | `/students/by-reg?registrationId=` | `Admin`, `Editor` | `SearchDatRegistration` |
| GET | `/students/lookup?search=` | `Admin`, `Editor` | `getsturegDetails` |
| GET | `/students/next-registration-no` | `Admin`, `Editor` | the `AVI/{n}/{yy}` sequence |
| GET | `/students/{id}/receipt` | `Admin`, `Editor` | `getPrint` → `PrintReport.cshtml` |
| GET | `/students/photo?registrationId=` | `Admin`, `Editor` | `ImageUpload` join on `Description` |
| POST | `/students` | `Admin`, `Editor` | `InsertStudents` → `usp_StudentSaveData` |
| PUT | `/students/{id}` | `Admin`, `Editor` | `Updatechanges` |
| DELETE | `/students/{id}` | `Admin` | `Deletechanges` (now a soft delete) |
| PATCH | `/students/activate?registrationId=` | `Admin`, `Editor` | `ActivateStudent` |
| GET | `/courses/lookup` | `Admin`, `Editor` | `Course/getDetails` |
| GET | `/courses/{id}/duration` | `Admin`, `Editor` | `Home/getCourseDetails` |
| GET | `/teachers/lookup` | `Admin`, `Editor` | `Teacher/getDetails` |
| GET | `/public/about` | anonymous | `Views/About/Index.cshtml` |
| POST | `/public/contact` | anonymous, rate-limited | `Insert into CusMessage ...` (§4.7 N4) |
| GET | `/health` | anonymous | new |

### POST `/auth/login`

```json
{ "email": "admin@avijan.local", "password": "Admin@12345" }
```

→ `data`:

```json
{
  "accessToken": "eyJhbGciOi...",
  "refreshToken": "base64…",
  "expiresIn": 3600,
  "user": {
    "id": 1,
    "name": "System Administrator",
    "email": "admin@avijan.local",
    "role": "Admin",
    "loginId": "admin",
    "landingRoute": "/dashboard"
  }
}
```

`landingRoute` carries the legacy role→page mapping (`AccountController.cs:75-140`)
so the client does not re-implement it: `Admin`/`Editor` → `/dashboard`,
`Student` → `/reports/students`, `Teacher` → `/reports/teacher`.

**Failure modes**

| Status | Case |
|---|---|
| 400 | validation (missing/invalid email, empty password) |
| 401 | unknown email **or** wrong password — deliberately indistinguishable |
| 403 | `IsActive = 0`, or `End_date` in the past |
| 429 | 5 failed attempts → 15-minute lockout; also 10 requests/min/IP |

### POST `/auth/refresh`

`{ "refreshToken": "…" }` → same payload as login. The presented refresh token is
**rotated on every use**, so a stolen token is single-shot.

### GET `/dashboard/summary`

Everything the Home Dashboard needs in one round trip:

```
counts           activeStudents, inactiveStudents, courses, teachers, pendingEnquiries
finance          feesCollectedThisMonth, otherIncomeThisMonth, expensesThisMonth,
                 netThisMonth, outstandingDues
trend[12]        { year, month, label, income, expense }  — gap-free, oldest first
courseEnrolment  { courseId, courseName, studentCount }   — active students only
recentNotices[5] { id, text, noticeDate }
recentAdmissions[5] { id, name, registrationId, courseName, dateOfJoining }
generatedOn      UTC timestamp
```

`income` on the trend line = monthly fee installments + other income.
`outstandingDues` = the latest `NewDue` per active student.

### GET `/public/about`

Everything the public front page needs, in one anonymous call.

Static copy comes from the `InstituteProfile` section of `appsettings.json`, so the
institute's description, address, phone and email are editable without rebuilding
the Angular app. The rest is read live from the database:

| Field | Source | Legacy origin |
|---|---|---|
| `courses` | `Course` | `Course/getDetails` |
| `notices` | `Notice`, newest 10 | `Notice/getDetails` |
| `routine` | `dailyRoutine` | `Notice/gettime` |
| `stats` | live counts | none — added here |

**The student roster is deliberately not exposed.** The legacy front page had a
*Show Student Details* button calling `Notice/StudentData` (`usp_ShowData`), which
published every active student's name, registration id, joining date, teacher and
course to anonymous visitors — personal data on roughly 240 people, with no login.
That panel is not reproduced. Staff read the same list from the student register,
behind a sign-in.

### POST `/public/contact`

```json
{ "name": "…", "email": "…", "phone": "…", "message": "…" }
```

Writes one `dbo.CusMessage` row with an explicit column list (replaces the legacy
positional insert, defect D6) and records `SourceIp`. Two independent guards:
3 requests / 10 min per IP (rate limiter) and one submission per email address per
5 minutes (service level).

## Security notes

- Passwords are BCrypt (work factor 12). The legacy plain-text `Password` column
  is not carried into this schema at all (defect D3).
- A login for an unknown email still runs a hash so response timing does not leak
  which addresses exist.
- No endpoint returns `PasswordHash`, `RefreshToken`, or any credential field.
- 401/403 from the JWT handler return the standard envelope, not an empty body.
- `Jwt:Key` is empty in `appsettings.json` on purpose; Development reads it from
  `appsettings.Development.json`, production must supply `JWT__KEY` via the
  environment (defect D12). The API refuses to start if the key is missing or
  shorter than 32 characters.

## Students

### Registration ids contain slashes

`AVI/3451/26` cannot go in a route segment: Kestrel decodes `%2F` before routing,
so the value splits across segments and never matches. Every endpoint keyed by
registration id therefore takes it as a **query parameter** —
`/students/by-reg?registrationId=AVI%2F3451%2F26`.

### `GET /students`

`?page=1&pageSize=10&isActive=true&courseId=&teacherId=&registrationId=&name=&search=`

`isActive` defaults to `true`; omit it explicitly (`isActive=`) for every status.
`registrationId` and `name` are the two legacy search boxes; `search` is a
free-text match across name, registration id, phone and email. Returns a
`PagedResult<T>` inside the usual envelope.

`hasPhoto` is true only when the photo can actually be served — see below.

### `POST /students`

The client does **not** send `registrationId` or `billNo`. Both are generated
server-side inside the insert transaction under `UPDLOCK, HOLDLOCK`, which is what
fixes the duplicate-registration-id defect (D4). The response carries the assigned
pair:

```json
{ "id": 3463, "registrationId": "AVI/3463/26", "billNo": "S-A-3463-26" }
```

`GET /students/next-registration-no` is a **preview only** — it takes no lock, so
the number it shows can be taken by another admission before you save.

### `DELETE /students/{id}` is a soft delete

Sets `IsActive = 0` and stamps `DOE`. The legacy `Delete Student where id=…`
destroyed the row and orphaned its fee ledger, marks and photo. Restricted to
`Admin`; `PATCH /students/activate` reverses it.

### `GET /students/photo`

Serves the decoded image with its real content type. Returns **404 with an
explanatory message** for the 94 students whose photo was stored by the older
uploader as a path on the web server's disk rather than as bytes in the database —
those files were never in the database to migrate. See `db/README.md`.

Because the endpoint requires a bearer token, an `<img src>` cannot load it
directly; the Angular client fetches the blob and builds an object URL.

### Validation

`StudentSaveRequest` is validated server-side with the same rules the legacy
`Scripts/Student.js` applied in the browser only: 10-digit phone, valid email,
required guardian, DOB in the past, DOJ after DOB, numeric fees, 1–3 teachers with
slot 2 and 3 required as the count demands. One rule is new: the same teacher
cannot fill two slots — the legacy `checkselectvalue` intended this but compared
the wrong elements and never fired.

## Users

`GET|POST /users`, `PUT /users/{id}`, `PATCH /users/{id}/status`,
`GET /users/roles`, `GET /users/email-exists?email=&excludingUserId=`,
`GET /users/birthdays/upcoming?days=30`.

Two deliberate differences from the legacy screen:

- **No password is ever returned.** The legacy grid selected `Password` and rendered
  it in a column. `hasPassword` is a boolean only.
- **A blank password on update keeps the current one.** The legacy form wrote
  whatever was in the box, so editing a user's phone number silently replaced their
  password with an empty string. Supplying a value here resets it *and* revokes the
  user's refresh token.

Create and update are `Admin` only; the list is open to `Editor` too.

### Handing a password to a member of staff

| Method | Path | Roles |
|---|---|---|
| POST | `/users/{id}/set-password` | `Admin` |

The legacy grid printed the plain-text `Password` column so an administrator could
read a teacher's or receptionist's sign-in back to them. That column is now a BCrypt
hash, so **there is no plaintext anywhere to display** — this is not a policy choice
that a screen could opt out of; the value is mathematically unrecoverable.

The equivalent capability is to *set* a known password and pass that on, which is
what this endpoint and the key icon on the Users grid do:

1. The dialog opens with a strong password already generated — 12 characters, at
   least one upper case, lower case and digit, drawn from `crypto.getRandomValues`.
   Look-alike characters (`0`/`O`, `1`/`l`/`I`) are excluded so it survives being
   written down or read out over the phone.
2. It is displayed in clear, in a plain text input, with **Copy** and **Generate
   again** beside it. The administrator can also type their own.
3. **Save** hashes it, stores it, and clears that user's refresh token so any
   session they already had ends.

The password is shown *before* saving and never again. Losing it costs nothing —
generate another. The same strength rules as the create form are enforced
server-side, so a password set this way can never be weaker than one typed into the
full user form.

Create and update are `Admin` only; the list is open to `Editor` too.

## Monthly course fees

| Method | Route | Replaces |
|---|---|---|
| GET | `/fees/bill?registrationId=` | `Course/getStuDetails` + `Pending.GetDuration` + `Due` |
| GET | `/fees/payments?registrationId=&page=&pageSize=` | `Course/MonthlyDetails` |
| POST | `/fees/calculate` | the arithmetic in `Scripts/MonthlyCourse.js` |
| POST | `/fees/payments` | `usp_SaveData` |
| PUT | `/fees/payments/{id}` | `usp_UpdateData` |
| GET | `/fees/payments/{id}/receipt` | `getPrint` → `PrintReport.cshtml` |

### The late-fine rule

A fee month is 32 days; beyond that it is ₹2 per day. Which date the clock runs from
depends on when the student joined — the institute changed scheme on **2019-11-01**,
a constant taken straight from the legacy JavaScript:

- **Joined on or after 2019-11-01** — measured from the joining date, and the elapsed
  days are divided across every month still outstanding, so paying several months at
  once is not fined as if each were late:
  `daysPerInstallment = daysElapsed / (installmentsPaid + 1)`
- **Joined before 2019-11-01** — measured from the previous payment, days used as they are.

Then `fine = max(0, daysPerInstallment − 32) × 2`.

`POST /fees/calculate` returns the figures **and** an `explanation` string spelling
out how it got there, which the page shows under the totals.

### What recording a payment writes

One transaction, four tables — the same set `usp_SaveData` touched:

| Table | Row |
|---|---|
| `MonthlyFees` | the ledger entry; `Installment` holds installment **+ fine** |
| `Fees` | the income roll-up the reports read |
| `LateFees` | the fine, always written so the fine report stays complete |
| `TecFees` | one payout per assigned teacher, `installment × percentage ÷ 100 ÷ teacherCount` |

The API does this in EF Core rather than through `usp_SaveData`, because that
procedure declares its money parameters as bare `decimal` — which SQL Server reads as
`decimal(18,0)` and silently rounds every amount to whole rupees.

Bill numbers are `S-M-{n}-{yy}` and are checked for collisions before use; the legacy
version did no such check.

### "Next payment due" on the receipt

**Payment date + 32 days.** The legacy `convert3()` anchored this to the *joining*
date for students on the new scheme — `joining + (monthsPaid + 1) × 32` — so a
student who had fallen behind got a receipt showing a date already in the past
(a payment taken 22 Aug 2026 printed "next due 3 Jul 2026"). Anchoring to the payment
date always reads forward and, unlike anchoring to the current date, does not change
if the receipt is reprinted later.

The admission receipt on the students page is unaffected: it still shows
joining date + 32 days, which is what `Scripts/Student.js` did and is correct there.

## Income report

`GET /income/report?from=&to=` and `GET /income/detail?section=&from=&to=&page=&pageSize=`.
Admin only, matching `[Authorize(Roles = "Admin")]` on the legacy `Income/Index`.

One date range drives everything. The legacy page had **seven independent date
ranges**, one per panel, each with its own running total and no consolidated figure —
the operator added them up by hand.

### Late fines are not separate income

`MonthlyFees.Installment` is written as *installment + fine*, so the fine is already
inside the fee figure. The report returns `lateFines` as an **"of which"** breakdown
and excludes it from `totalIncome`. Verified across the data: of 1,439 fined rows,
every one has `fine <= Installment` and none exceeds it.

The legacy page showed fee income and late fines as two panels with two totals,
which invited exactly this double count. The dashboard had the same bug and is fixed.

### Two more corrections

- **`UNION` → `UNION ALL`.** The legacy roll-up (`Models/Income.cs:39`) unioned fee
  rows with admission-fee rows. `UNION` de-duplicates, so two payments of the same
  amount, on the same day, for the same student collapsed into one. For 2025 alone
  that understated income by ₹475 (₹13,954 reported vs ₹14,429 actual).
- **No `Math.abs()` on totals.** `Scripts/Income.js` took the absolute value of every
  row before summing, turning credits and negative dues into positive amounts.

### What it returns

```
income   feeCollections + admissionFees + otherIncome        = totalIncome
         of which lateFines                                  (breakdown only)
cost     expenses + teacherPayouts + teacherExpenses         = totalCost
result   netProfit, marginPercent
         duesMovement  (change in outstanding balances; not income)
         sections[]    per-section totals and row counts
         monthly[]     income / cost / net per month (ranges up to 36 months)
```

Totals come from SQL aggregates, so they stay correct however many detail rows the
range holds. `/income/detail` pages the rows behind any one section.

## Where the pieces live

| Concern | Path |
|---|---|
| Controllers | `Avijan_Migration_API/src/InstituteMS.Api/Controllers/v1/` |
| DTOs, validators, service interfaces | `Avijan_Migration_API/src/InstituteMS.Application/` |
| Service implementations, EF Core, JWT, BCrypt | `Avijan_Migration_API/src/InstituteMS.Infrastructure/` |
| Entities | `Avijan_Migration_API/src/InstituteMS.Domain/Entities/` |
| Typed clients + models | `Avijan_Migration_Angular/src/app/core/` |

## Teachers

`GET|POST /teachers`, `PUT /teachers/{id}`, `DELETE /teachers/{id}` (Admin),
`GET /teachers/login-exists?loginId=&excludingTeacherId=`,
`GET /teachers/{id}/earnings?from=&to=&page=&pageSize=`.

Same three fields as the legacy screen (name, user id, phone) and the same
duplicate-login check (`ufnGetTecChk`), plus each teacher's student count and
lifetime earnings.

**Delete is guarded.** The legacy `Delete Teacher where id=...` had no referential
check, which is exactly how 53 students and 527 payout rows ended up pointing at
teachers that no longer existed (defect T4). A delete now returns `409` naming what
still references the teacher:

> Anindita Mukherjee cannot be deleted - 12 student(s), 30 payout record(s) still
> reference this teacher. Reassign them first.

Rows flagged `isPlaceholder` are the migration's stand-ins for those already-deleted
teachers. Editing one and giving it a real name clears the flag, which is how the
data gets cleaned up.

## Branches

`GET /branches`, `POST /branches` (Admin), `PUT /branches/{id}` (Admin),
`PATCH /branches/{id}/status` (Admin).

The legacy `Views/Branch/Index.cshtml` was an empty stub, but the table drives
registration ids — Rajpur's code `Raj` produces `AVI/Raj/3463/26`.

**The code is frozen once used.** Changing it would leave already-issued ids carrying
a code that maps to nothing, so the API refuses:

> The code cannot be changed: 5 registration id(s) were already issued as AVI/Raj/...
> Rename the branch if you need to, but leave the code alone.

Renaming *is* allowed: `Student.Branch` stores the name, so existing students are
carried over to the new name in the same transaction.

## Offer and experience letters

Client-side only — no endpoint. Ported from `generateOfferLetter` and
`generateReleaseLetter` in `Scripts/Users.js`.

Both are restricted to the three staff roles the legacy page gated on:
**Receptionist**, **Receptionist & Teacher**, **Teacher**. The experience letter
additionally requires `End_date` to be set. Everyone else shows *Not Required* in the
Emp. id column, exactly as before.

The offer letter's CTC breakup keeps the legacy percentages — Basic 50, HRA 20,
Conveyance 4.8, Medical 3.75, Special 21.45 — which total 100, so the breakup always
adds back to the CTC entered.

Two changes from the legacy implementation:

- **No html2canvas/jsPDF.** The old code rasterised a hidden div into a bitmap and
  wrapped it in a PDF page, giving blurry non-selectable text and two large
  dependencies. The letter now opens in a print window, where the browser's own
  *Save as PDF* produces real vector text.
- **The CTC prompt is a dialog, not `window.prompt()`**, which could not validate and
  is blocked outright in some browsers.

One legacy bug is fixed in passing: the release-letter template repeated
`id="rl_empName"` three times and only the first was ever populated, so the sentence
rendered as "He/She has been released…" with no name in front of it.

---

## Courses

| Method | Path | Legacy origin |
|---|---|---|
| GET | `/courses?page=&pageSize=&search=` | `Course/getCourseDetails` |
| GET | `/courses/lookup` | `Course/getDetails` |
| GET | `/courses/name-exists?name=&excludingCourseId=` | new |
| GET | `/courses/progress?page=&pageSize=&courseId=&registrationId=` | `CourseDuration` (three actions, see below) |
| GET | `/courses/{id}/duration` | `Home/getCourseDetails` |
| POST | `/courses` | `Course/InsertCourse` |
| PUT | `/courses/{id}` | `Course/UpdateCourse` |
| DELETE | `/courses/{id}` | `Course/DeleteCourse` |

All routes require `Admin` or `Editor`.

### The three duration reports collapse into one

The legacy project had `CourseDuration/getDetails` (everyone),
`CourseDuration/SearchByCourse` and `CourseDuration/SearchByRegistration` — three
actions running three near-identical queries. They are one endpoint here:
`GET /courses/progress`, with `courseId` and `registrationId` as optional filters.
Passing neither returns every active student, which is what the first action did.

`paidMonths` is counted from the fee ledger, not from months elapsed since joining —
the same rule the legacy `Pending.GetDuration` used, so a student who has paid
nothing for a year still reads 0 months completed rather than 12.

### Delete is guarded

The legacy delete removed the Course row outright, leaving every `Student.Cid`
pointing at a course that no longer existed — the grid then rendered a blank course
column for those students. Deleting a course with students enrolled now returns
`success: false` naming the course and the enrolled count (plan §4.2 C4).

### Duplicate names are rejected

Course names are compared case-insensitively and trimmed. The Angular form checks
`name-exists` as you type (debounced) so the clash surfaces before submitting; the
server re-checks regardless.

---

## Marks

| Method | Path | Legacy origin |
|---|---|---|
| GET | `/marks?page=&pageSize=&registrationId=&search=` | `Marks/getDetails`, `Marks/SearchData` |
| POST | `/marks` | `Marks/InsertMarks` → `usp_InsertMarkData` |
| PUT | `/marks/{id}` | `Marks/UpdateMarks` |
| DELETE | `/marks/{id}` | `Marks/DeleteMarks` |

All routes require `Admin` or `Editor`.

### The legacy column names are inverted from how they read

`Marks.M1` is the **full marks** for subject 1 and `OM1` is the **marks obtained** —
not the other way round, despite `OM` looking like the abbreviation for the total.
This was confirmed against the data before the entities were named: all 36 migrated
rows satisfy `M1 >= OM1`, and the sample row `Sub1='IT Theory', M1=50, OM1=30` is
only coherent one way. The C# properties are therefore `FullMarks1` / `ObtainedMarks1`
mapped onto columns `M1` / `OM1`.

### Four subject slots, kept

The table has four fixed `Sub/M/OM` triples rather than a child table. That is
preserved as-is — normalising it would have meant migrating and re-keying the marks
history for no gain to the four-subject form the institute actually uses.

A blank subject clears its marks with it, so a slot emptied on an edit leaves no
orphaned numbers behind. At least one subject must be filled.

### Validation the legacy form did not have

`usp_InsertMarkData` took twelve positional parameters and validated none of them, so
marks obtained could exceed the full marks and full marks could be zero or negative.
Per filled slot the API now requires: full marks present and greater than zero, marks
obtained present and not negative, and obtained ≤ full. The registration id must match
an existing student. The Angular form mirrors these rules so the operator is told
before the round trip.

`totalFullMarks`, `totalObtainedMarks` and `percentScored` are computed on read — the
legacy grid showed the raw per-subject numbers only and left the totalling to the eye.

---

## Notices

| Method | Path | Legacy origin |
|---|---|---|
| GET | `/notices?page=&pageSize=&search=` | `Notice/getDetails` |
| GET | `/notices/server-time` | `Notice/GetServerTime` |
| POST | `/notices` | `Notice/InsertNotice` |
| PUT | `/notices/{id}` | `Notice/UpdateNotice` |
| DELETE | `/notices/{id}` | `Notice/DeleteNotice` |

All routes require `Admin` or `Editor`. The five most recent notices are what
`/dashboard/summary` puts on the board.

### Editing no longer moves a notice to the top

The legacy update stamped `NoticeDate = GETDATE()` on every save, so correcting a typo
in a two-year-old notice re-dated it and pushed it back onto the dashboard board.
`noticeDate` is now an explicit optional field: omitted on create it stamps the current
time, omitted on update it leaves the stored date untouched. Send a value to change it
deliberately.

### Notice text

`text` is required after trimming (the legacy form accepted a whitespace-only notice
and published a blank row) and capped at 4000 characters to match the column. The
Angular form is a textarea with a live character count rather than the legacy
single-line input, since notices routinely run to several sentences.

---

## Reports

| Method | Path | Roles | Legacy origin |
|---|---|---|---|
| GET | `/reports/admissions?from=&to=&courseId=&page=&pageSize=` | `Admin`, `Editor` | `StuReport/GetAllStuDetails` |
| GET | `/reports/dues?courseId=&from=&to=&includeNeverPaid=&page=&pageSize=` | `Admin`, `Editor` | `usp_NotPaidData` via `Income/NotPaid` |
| GET | `/reports/expenses?from=&to=&search=&page=&pageSize=` | `Admin`, `Editor` | `ExpenseReport/GetAllExpenseDetails` |
| GET | `/reports/teacher-earnings?teacherId=&from=&to=` | `Admin`, `Editor` | `Income/TeacPaid` + `TeacherExpense/TeacPaid` |
| GET | `/reports/students-by-teacher?teacherId=&activeOnly=&search=&page=&pageSize=` | `Admin`, `Editor` | `StudentTecacherReport/getStudentTeacherDetails` |
| GET | `/reports/student-statement?registrationId=` | any role | `StudentReport/getStuDetails` + `StudentDetails` + `Marks/getStuDetails` |
| GET | `/reports/my-registration-id` | any role | `StudentReport/getStudentDetails` |

Six legacy controllers collapse into one. `TeacherDetailsReportController` and
`StuReportController` are gone entirely: both were unauthenticated Web API
controllers duplicating queries the MVC controllers already ran.

### Two things changed in every report

**Totals are SQL aggregates.** Each legacy page summed rows in the browser with
`ng-init="setTotalIncome(Emp)"`, which fires once per *rendered* row. Paging or
re-sorting the grid silently changed the total, and several of those helpers called
`Math.abs()` on each row, so a correcting negative entry was added instead of
subtracted. Every total here covers the whole matching set, not the page on screen.

**Parameters are parameters.** Every report concatenated dates and ids straight into
SQL (defect D1). The dates went through `Convert.ToDateTime(...).ToString()` under
the server's locale first, so the same range could be read day/month or month/day
depending on the machine.

One report was worse than the rest: the admissions page called
`http://www.avijan.in/api/StuReport/GetAllStuDetails` **from the browser**
(Scripts/APIStudent.js), so it queried the production server from whatever machine
had the page open, over plain HTTP, with no authentication. It is served by this
application now and needs a token like everything else.

### `GET /reports/dues` — the rule and the omission

The legacy procedure was `(students who paid outside the range) EXCEPT (students who
paid inside it)`. That EXCEPT is the actual business rule and is preserved exactly:
run with `includeNeverPaid=false` and the row count matches the procedure
(verified — BCA over 2025 returns 4 rows either way).

Both halves of the procedure inner-joined to `MonthlyFees`, which was not a rule but
an accident. A student with no fee row matched neither half, so **the report on
unpaid students silently omitted everyone who had never paid at all** — the students
furthest behind. They are included by default and flagged `hasEverPaid: false`.
Same course and range: 4 rows the legacy way, 8 with the never-paid students restored.

Two columns the legacy report did not carry are added: `lastPaymentDate` and
`outstandingDue`, taken from the student's most recent ledger row.

### `GET /reports/teacher-earnings`

Earnings and payouts for one teacher, with `pending = earned − paid`.

The legacy earnings query required the student to *still* list this teacher in one of
their three slots (`C.TecName = id OR C.TecName1 = id OR ...`) **on top of** matching
`TecFees.tecid`. Reassigning a student to a different teacher therefore erased the
original teacher's past earnings from their own report. The `TecFees` row already
records who was paid at the time, so it decides alone here; only the course name is
read from the student.

A collation trap worth recording: `TecFees` holds registration ids in mixed case
(`Avi/2324/22` beside `AVI/2324/22`). SQL Server matched them anyway under its
case-insensitive collation, but the in-memory join that attaches student names did
not, and names came back blank until it was made `OrdinalIgnoreCase`.

### `GET /reports/student-statement`

Profile, full fee ledger and all exam results in one response. The legacy page made
four chained round trips — resolve the id from the session, then the profile, then
the ledger, then the marks — each firing from the previous callback.

**Access.** Office staff pass any `registrationId`. A student may only read their own:
the id is resolved server-side from their signed-in email and a mismatched id returns
403. The legacy page compared two ids in JavaScript and returned `false` on a
mismatch, which stopped nothing — the action behind it accepted any id posted to it.

**Instalment and fine are separate columns.** `MonthlyFees.Installment` is stored with
the fine already inside it, so the legacy "monthly fees" column silently included
fines. `instalmentOnly = amountPaid − fine` here.

**Grades count every subject.** The bands are the legacy certificate rule exactly —
95 AA, 90 A+, 80 A, 75 B+, 60 B, 50 C, below that D — but `convertgrade` in
Scripts/StudentReport.js totalled subjects 1 and 2 only, so a four-subject exam was
graded on half its papers.

Only active or completed students have a statement, matching the legacy
`(IsActive = 1 or IsComplete = 1)` filter.

### Exports

Every report exports the **whole result set**, not the page on screen, as CSV with a
UTF-8 BOM so Bengali names survive the trip into Excel. The one legacy export — the
students-by-teacher page — wrote an HTML `<table>` into a file named `.xls`, which
recent Excel opens with a format warning, and used raw column names as headers
(`Stuname`, `NoTech`, `TecName1`) beside the teacher *ids* those columns hold rather
than names.

---

## Expenses

| Method | Path | Roles | Legacy origin |
|---|---|---|---|
| GET | `/expenses?from=&to=&search=&page=&pageSize=` | `Admin`, `Editor` | `Expense/getDetails` |
| POST | `/expenses` | `Admin`, `Editor` | `Expense/InserExpenses` |
| PUT | `/expenses/{expenseId}` | `Admin`, `Editor` | `Expense/Updatechanges` |
| DELETE | `/expenses/{expenseId}` | **`Admin`** | `Expense/Deletechanges` |

The list response carries `rows` and a `summary` holding `entryCount` and
`totalAmount` for everything the filter selects, not just the page returned.

### The write actions had no authorisation at all

Only `Index` and `ExpenseReport` — the two actions that merely render a page —
carried `[Authorize(Roles = "Editor,Admin")]`. `InserExpenses`, `Updatechanges` and
`Deletechanges`, the three that actually change money records, had no attribute, so
any signed-in user of any role could post to them directly. The roles are enforced on
the operations here, and deletion is narrowed to `Admin` because it silently changes
every income and expense report that covers the date.

### Validation the legacy form did not have

The legacy insert was `Insert into Expense values ('...', '...', ...)` — string
concatenation, no column list, and no checks. Now: details required and capped at 500
characters, amount greater than zero and under 10,00,000 (the column is
`decimal(9,2)`, so larger values could not be stored anyway), and the date may not be
in the future. A mistyped year used to save happily and then fall outside every
report range, making the entry effectively invisible.

### An omitted date means different things on create and update

On **create**, a null `expenseDate` means today — what the legacy picker defaulted to.
On **update**, a null keeps the date already stored. The legacy update wrote whatever
the form held, so re-saving a row after correcting a typo in the details could move
the expense to a different day and shift it between reporting periods.

### Deletion stays a hard delete

Nothing references `Expense`, so there is no orphan to create and the row is removed
outright, as before. What is new is the confirmation dialog naming the entry and its
amount — the legacy grid deleted on a single click with no prompt.

---

## Examination portal

New in this system — the legacy application had no online examination. Two
controllers, split by audience.

### Administrator — `/exams`

| Method | Path | Roles |
|---|---|---|
| GET | `/exams?search=&courseId=&page=&pageSize=` | `Admin`, `Editor` |
| GET | `/exams/{examId}` | `Admin`, `Editor` |
| POST | `/exams` | `Admin`, `Editor` |
| PUT | `/exams/{examId}` | `Admin`, `Editor` |
| DELETE | `/exams/{examId}` | **`Admin`** |
| PATCH | `/exams/{examId}/published?isPublished=` | `Admin`, `Editor` |
| GET | `/exams/{examId}/questions` | `Admin`, `Editor` |
| POST | `/exams/{examId}/questions/upload` | `Admin`, `Editor` |
| GET | `/exams/questions/template` | `Admin`, `Editor` |
| GET | `/exams/{examId}/practical/roster` | `Admin`, `Editor` |
| POST | `/exams/practical/marks` | `Admin`, `Editor` |
| GET | `/exams/{examId}/schedule` | `Admin`, `Editor` |
| POST | `/exams/schedule` | `Admin`, `Editor` |
| POST | `/exams/schedule/batch` | `Admin`, `Editor` |
| POST | `/exams/{examId}/schedule/{registrationId}/regenerate-code` | `Admin`, `Editor` |
| DELETE | `/exams/{examId}/schedule/{registrationId}` | `Admin`, `Editor` |

### Student — `/my-exams`

| Method | Path | Roles |
|---|---|---|
| GET | `/my-exams` | **`Student`** |
| POST | `/my-exams/{examId}/start` | **`Student`** |
| POST | `/my-exams/submit` | **`Student`** |
| GET | `/my-exams/results` | **`Student`** |
| GET | `/my-exams/course-results` | **`Student`** |
| GET | `/my-exams/admit-cards` | **`Student`** |

Every `/my-exams` call resolves the student from the signed-in email rather than
trusting an id from the browser, so one student cannot read or sit another's paper
by changing a parameter.

### A course is examined in terms of two papers

Each examination of a course is a **term**, carrying one **theory** paper and one
**practical**. The number of terms is not capped — `TermNumber` need only be
positive, and the pair is keyed on `(StuRegId, Cid, TermNumber, PaperType)`, so a
student sits each paper of each term once, course by course.

Theory is sat in the portal and marked automatically. A practical is examined in the
room, so it carries no questions and no clock — an administrator types the mark in
from the paper's roster.

A term's grade is withheld until **both** its papers are in. A grade struck on half
an examination would misrepresent it, so the term reports `isComplete: false` and a
null grade until the practical is entered.

### A sitting is set per student, and the code is what opens the paper

`ExamPaper.OpensOn`/`ClosesOn` bound the paper as a whole. Within that, the
institute schedules **each student individually** — it sits them in batches, not
all at once — and `dbo.ExamSchedule` holds one row per (paper, student) carrying
the date and time and a six-digit `AccessCode`.

Two things follow from that row, and neither is possible without it:

- **The admit card.** `GET /my-exams/admit-cards` returns nothing until the
  student is scheduled. There is no date to print before then.
- **Opening a theory paper.** `POST /my-exams/{examId}/start` now takes
  `{ accessCode }`. It refuses with **403** when the student has no schedule,
  **400** before their scheduled time, and **403** when the digits are wrong.

The code is deliberately a *second* factor, separate from the sign-in.
Credentials say who you are; the code says the institute has you sitting here,
now. Without it a student with a working password could open their paper early,
from home, or someone holding a borrowed password could sit it for them.

**The sitting sheet lists only students still enrolled.** Someone who has finished
the course does not come back in to sit an examination, so listing them buries the
people who do — on one real course the sheet ran to 43 names of which 5 were
enrolled, and three finished students were scheduled by mistake because the roster
made them look current. `schedule/batch` follows the same rule, so "schedule
everyone" cannot sweep a finished student back in, and `POST /exams/schedule`
refuses one outright.

The one exception: a student who already has a sitting or a mark on that paper
stays on the sheet whatever their status, flagged *Course completed*. Their row has
to remain reachable so the sitting can be corrected or withdrawn — otherwise
someone who completes the course mid-cycle would simply vanish along with their
booking. Moving such a sitting is allowed for the same reason.

Design points worth stating:

- **One code per student, never one per room.** A shared code would be a single
  number the whole room could pass along. `schedule/batch` sets one date for
  everyone and still issues a distinct code to each.
- **Codes come from `RandomNumberGenerator`, not `Random`.** The point of the
  code is that it cannot be worked out ahead of the sitting, and `Random` seeded
  from the clock can be. Stored as `char(6)` text so a leading zero survives.
- **Compared in fixed time.** Six digits is a small enough space that a timing
  signal is worth denying, so the check does not exit on the first differing
  digit.
- **Checked on resume, not only on first open.** Picking the paper up on another
  machine needs the code again.
- **Never sent to the student, and never printed on the admit card.** The codes
  appear on the administrator's scheduling sheet and nowhere else; the
  invigilator reads one out at the desk. `GET /my-exams` deliberately does not
  select the column. If a code is overheard, `regenerate-code` issues a new one
  and the old stops working immediately.
- **A practical has no code.** It is examined in the room, so its schedule row
  exists only to print the date beside the theory one on the admit card.
- **A sitting cannot be moved once the student has opened the paper**, and
  cannot be withdrawn either — the card they are holding would be wrong and the
  rules would change under them mid-exam.

### The answer key never leaves the server

`ExamQuestionForStudentDto` has no correct-answer field at all — not a nulled one, an
absent one — so the key cannot be read out of the browser's network tab mid-exam.
Marking happens server-side, and the result is **stored** rather than recomputed, so
editing the question bank afterwards cannot silently change a mark a student has
already been told.

### The clock is the server's

The deadline is fixed when the attempt opens and written to the row. Reloading the
page, or editing the countdown in the console, buys nothing; the on-screen timer is
cosmetic. Resuming returns the same deadline, so closing the browser does not restart
the clock. A submission arriving after the deadline is still marked — on the answers
that arrived — and flagged `autoSubmitted`.

### The question sheet reports every fault at once

`POST /exams/{examId}/questions/upload` takes an `.xlsx` with the columns Question,
Option A, Option B, Option C, Option D, Correct Option, Marks. Nothing is saved
unless every row is valid, and the response lists **all** the problems rather than
stopping at the first, so the sheet can be corrected in one pass. A workbook that
cannot be opened at all is refused with plain advice, and the underlying exception is
logged — otherwise a malformed sheet and a bug in the parser look identical.

### Publishing checks the marks add up

A theory paper publishes only once its uploaded question marks equal the paper's
total, so a student can never sit a paper worth less than it claims. A practical has
no questions and publishes straight away. A paper anyone has sat cannot be deleted or
have its questions replaced — that is the evidence behind a result already given —
and it cannot be edited while a sitting is in progress.

### Questions live in one table

`ExamQuestion` is deliberately denormalised — the four options and the key are
columns on the row, mirroring the shape `dbo.Marks` already uses for its four subject
slots. One table per concern keeps the eventual production migration a straight copy.

### The three printed documents keep their legacy designs

The money receipt, the certificate and the admit card are documents people
already recognise across a counter, so they reproduce their legacy layouts
exactly rather than being redesigned:

| Document | Legacy view | Where it prints from |
|---|---|---|
| Money receipt | `Views/MonthlyCourseFees/PrintReport.cshtml` + `Content/style1.css` | Fees screen, and the student's own statement |
| Certificate | `Views/Home/PrintCertificate.cshtml` | Student statement |
| Exam admit card | `Views/Admit/PrintAdmit.cshtml` | Student examination portal |

All three live in `core/printing/legacy-documents.ts` — three screens print them
and three copies of the same markup would drift apart. The original artwork
(`file_trans.png`, `SAVE_20191128_212413.jpeg`) is the same file the legacy site
served.

Two deliberate departures from the legacy pages, neither of which changes how the
documents look:

- **They print the markup instead of rasterising it.** The legacy pages pulled
  `html2canvas`/`html2pdf`/`jsPDF` from a CDN and turned the page into a bitmap
  at scale 3 — a large, soft image with no selectable text. Printing the markup
  keeps the text sharp and fetches nothing from outside.
- **The admit card's Date and Time cells now carry real values.** The legacy card
  printed that table blank for someone to fill in by hand. It is filled from the
  student's schedule, which is the reason the card is now issued per student at
  all. It still prints two copies to a sheet with a cut line between them, as the
  legacy PDF did.

### Online fee payment — recorded on the student's word, confirmed afterwards

A student pays their monthly fee from the portal against the institute's **static**
UPI QR code. Static matters: there is no gateway and no callback, so **nothing tells
the server the money arrived**. The student types the UPI reference from their own
app and the payment is recorded on that.

| Endpoint | Role | |
|---|---|---|
| `GET /my-fees/due` | Student | fee, late fine, total, UPI details for the next unpaid month |
| `POST /my-fees/pay` | Student | records it — body is the reference only |
| `GET /fees/online/unverified` | Back office | the dashboard notification list |
| `POST /fees/online/{feeId}/verify` | Back office | marks it checked |

Paying writes exactly what the counter writes — `MonthlyFees`, `Fees`, `LateFees`
and one `TecFees` row per teacher — so the ledger and the teacher payout are right
immediately. Three columns on `MonthlyFees` carry the rest:

- **`TransactionId`** — the UPI reference. `NULL` on a payment taken at the
  counter, which is how online and offline are told apart. Uniquely indexed, so the
  same reference cannot be submitted twice.
- **`VerifiedOn` / `VerifiedBy`** — `NULL` means recorded but not yet checked
  against the Paytm account. That is precisely what the dashboard lists.

Points worth stating:

- **The amount never comes from the browser.** `POST /my-fees/pay` accepts only the
  reference; the fee and fine are recomputed server-side by the same
  `LateFineCalculator` the counter uses, so a student cannot choose what their
  month costs.
- **The teacher percentage is carried over from the student's previous payment** —
  nothing else records what was agreed. A student with no previous payment is told
  their first payment is taken at the centre, because there is no percentage to
  carry and guessing one would mispay a teacher.
- **One month at a time**, the next one due. The ledger advances a month per
  payment, so offering several would need each dated and fined separately.
- **`CreatedBy` is `portal:{registrationId}`**, so the ledger never suggests a
  member of staff handled money they never touched.

**The standing risk, by design.** A payment counts the moment the student submits
it. Someone who types a plausible reference without paying clears their dues and
credits a teacher's share until the office notices. That is the accepted trade-off
for recording immediately rather than on confirmation; the verification columns are
what make such a payment findable, and correcting one means amending the fee row on
the Monthly Fees screen.

### Results live in two stores, and the statement reads both

There are two places a mark can exist, and they are not the same thing:

| Store | Written by | Feeds |
|---|---|---|
| `dbo.Marks` | the **Marks** screen (legacy register) | student statement, certificate |
| `ExamAttempt` | the **examination portal** — theory submitted, practical entered on the paper's mark sheet | My Exams |

They are kept separate on purpose: a `dbo.Marks` row is free-text
(`Sub1 = 'PRACTICAL'`, 49/50) with no link to a course, term or paper, so it cannot
be matched to "the practical of examination 1" reliably.

The consequence was a real defect. **My Statement and the certificate originally
read `dbo.Marks` alone**, so a student who sat their whole course through the
portal had an empty marks section and was refused a certificate outright — *"no
exam result has been recorded"* — however many papers they had actually sat. The
statement and certificate now read **both** stores:

- **Statement** — legacy rows and portal examinations together, newest first, so
  one screen shows the whole record.
- **Certificate** — portal results win when the student has a finished examination
  there, since that is the course they actually sat; `dbo.Marks` remains the
  fallback for migrated students. Every finished examination counts, not just the
  latest, because the certificate states the course result rather than one term's.

**An examination contributes only once both its papers are in.** A half-finished
term would otherwise show as a low percentage and drag the certificate grade down —
the same reason the portal withholds a term grade until the practical is entered.

The **Download Certificate** button is disabled until the student's record is
marked complete, matching the server, which refuses a certificate to anyone still
enrolled.

### The first certificate is the original; every one after it is a duplicate

`POST /reports/my-certificate` **issues** a certificate and records the copy in
`dbo.CertificateIssue`. The first call returns the original. From the second call
onwards the response carries `isDuplicate: true` and an `issueNo`, and the printed
certificate shows a red **DUPLICATE COPY** watermark across the artwork plus a
corner stamp reading the copy number, the running total, and the date the original
was taken.

| Call | `issueNo` | `isDuplicate` | Printed as |
|---|---|---|---|
| 1st | 1 | false | the original, unmarked |
| 2nd | 2 | true | DUPLICATE COPY — No. 2 |
| 3rd | 3 | true | DUPLICATE COPY — No. 3 |

Three decisions worth stating:

- **POST, not GET.** Issuing is not a read. A GET would be repeated by refreshes,
  back buttons and caches, inflating the count without anyone asking for a copy.
- **No non-recording variant.** A second endpoint returning the same document
  without counting it would be the obvious way to obtain an unmarked second copy,
  which is exactly what the marking exists to prevent.
- **A log, not a counter column.** "When was the second copy taken, and by which
  sign-in?" is the question the office actually gets asked, and a counter cannot
  answer it. `IssueNo` is unique per student, so two copies can never share a
  number.

The eligibility rule is unchanged and still applies first: a student who has not
completed the course gets no certificate at all, original or duplicate, so no
issue is recorded.

### The certificate rule is unchanged

Sitting examinations does not unlock the certificate. As in the legacy application, it
becomes available only once the course is completed and the institute marks the
student's record complete; `GET /reports/student-certificate` refuses otherwise.

---

## Roles, audited against the legacy attributes

Every legacy `[Authorize(Roles = ...)]` was read off the old controllers and compared
with what this API enforces. Three differences were bugs on our side and are fixed;
the rest are deliberate and listed below with the reason.

### Where the legacy roles are reproduced exactly

| Legacy page | Legacy roles | Here |
|---|---|---|
| `Home/Index` (students) | Admin, Editor | `/students/*` |
| `MonthlyCourseFees/Index` | Admin, Editor | `/fees/*` |
| `Course/Index` | Editor, Admin | `/courses` master |
| `Marks/Index` | Editor, Admin | `/marks/*` |
| `Notice/Index` | Admin, Editor | `/notices/*` |
| `Teacher/Index` | Editor, Admin | `/teachers/*` |
| `Branch/Index` | Editor, Admin | `/branches/*` |
| `Register/Index` (users) | Admin, Editor | `/users` read |
| `Expense/Index` | Editor, Admin | `/expenses` |
| `Income/Index` | **Admin only** | `/income/*` |
| `StudentReport/MonthlyIndex` | Editor, Admin | `/reports/admissions` |
| `StudentReport/NotPaidIndex` | Editor, Admin | `/reports/dues` |
| `Expense/ExpenseReport` | Editor, Admin | `/reports/expenses` |
| `TeacherReport/Index` | Editor, Admin, **Teacher** | `/reports/teacher-earnings` |
| `CourseDuration/ByCourse` | Admin, Editor, **Receptionist** | `/courses/progress` (filtered) |
| `CourseDuration/Index` | Admin, Editor | `/courses/progress` (unfiltered) |
| `StudentReport/Index` | **Student** | `/reports/student-statement` |
| `About/Index` | anonymous | `/public/*` |

### Three bugs the audit found

**Landing routes pointed at pages that do not exist.** `UserRoles.LandingRoute`
returned `/reports/teacher`, `/reports/students` and `/reception`. None of those are
real Angular routes, so a Teacher, Student or Receptionist signed in successfully and
was then bounced through the wildcard route to the public About page — indistinguishable
from a failed login. They now return `/reports/teacher-earnings`,
`/reports/student-statement` and `/courses`.

**A Teacher could not open their own account.** `TeacherReport/Index` was
`[Authorize(Roles = "Editor,Admin,Teacher")]` and resolved the teacher from
`Session["UserID"]`. `/reports/teacher-earnings` was back-office only. Teacher is
admitted again, pinned to the record whose `LoginId` matches their sign-in: passing
someone else's `teacherId` returns 403, and omitting it resolves to their own.

**A Receptionist could not run the course-duration report.** The legacy
`ByCourse`/`CoursesByCid`/`CoursesByCidAndStuRegId` trio was
`Admin,Editor,Receptionist`, while the unfiltered `CourseDuration/Index` was
`Admin,Editor`. Because all three collapse into one endpoint here, a Receptionist is
admitted only *with* a `courseId` or `registrationId`; unfiltered still requires a
back-office role, so they do not gain the whole-institute view the legacy project
withheld. The Angular page shows them the progress panel alone, with no course master.

A fourth, found while testing the above: **the teacher validator rejected the login id
format the data actually uses.** `TeacherSaveRequestValidator` allowed only
`[A-Za-z0-9._-]`, but 16 of the 26 live teacher rows hold an email in `LoginId` —
which is precisely what the self-service lookup matches on. Editing any of those 16
would have failed validation. `@` is now allowed, within the column's 20 characters.

### Where this API is deliberately stricter

The legacy project put `[Authorize]` on the **page**, almost never on the action
behind it. Every `savechanges`, `Updatechanges`, `Deletechanges`, `InserExpenses` and
`InsertData` was reachable by any signed-in user of any role — a Student's token could
post to them directly. Two controllers had no attribute at all:
`StudentTecacherReportController`, which returned every student's address, phone and
email for any teacher id, and `StuReportController` / `ExpenseReportController`, plain
Web API classes with no authentication whatsoever.

Roles are enforced on operations here, not on pages. On top of the legacy roles:

| Operation | Restricted to | Why |
|---|---|---|
| Delete a course, mark, student or expense | Admin | Silently rewrites historical reports |
| Create or update a user | Admin | Grants sign-in and roles |
| `/reports/students-by-teacher` | Admin, Editor | Was unauthenticated; exposes personal contact data |


### The student statement is Student-only

`StudentReport/Index` carried `[Authorize(Roles = "Student")]`, and that is kept
exactly: `/reports/student-statement` and `/reports/my-registration-id` admit the
**Student role and nobody else** — Admin, Editor, Teacher and Receptionist all get
403. The screen shows a student their entire record, so staff are kept out of it by
design; they read the same underlying data through the student register and the fee
ledger instead.

The endpoint takes no registration id at all — it is resolved server-side from the
signed-in email, so there is nothing to pass and nothing to tamper with. The legacy
page compared two ids in JavaScript and returned `false` on a mismatch, which stopped
nothing: the action behind it accepted any id posted to it.

### Not migrated

`Admit` (Admin) and `Reception/Resignation_portal` (Admin, Receptionist) have no
counterpart yet. A Receptionist lands on `/courses` in the meantime.

### The two download buttons on the student report

The live page at `www.avijan.in/StudentReport` carries a **Download** button on every
fee row and a **Download Certificate** button at the foot. Both are migrated, Student
role only.

| Method | Path | Legacy origin |
|---|---|---|
| GET | `/reports/my-receipt?feeId=` | per-row *Download* → `MonthlyCourseFees/getPrint` → `PrintReport.cshtml` |
| GET | `/reports/my-certificate` | *Download Certificate* → `Home/getPrintCertificate` → `PrintCertificate.cshtml` |

**The receipt now checks ownership.** The legacy button posted the grid row object
straight back to `MonthlyCourseFees/getPrint`, so a crafted request could print
anyone's receipt. `my-receipt` resolves the caller's own registration id and returns
403 for a `feeId` belonging to someone else — verified.

**The certificate no longer travels through the query string.** The legacy flow
fetched the data, computed the grade in JavaScript, then redirected to
`Home/PrintCertificate?Stuname=…&CName=…&Grade=…`. Every field on the printed
certificate came from the URL, so anyone could edit it and print a certificate naming
any person, course and grade. Nothing is passed in here: name, guardian, registration
id, course, dates and grade are all read server-side from the signed-in student's own
record.

Other differences:

- **A student with no exam result gets a message, not a blank certificate.** The
  legacy query inner-joined `Marks`, so a student without marks received a
  certificate with empty fields. There is no grade without marks, so this returns a
  stated failure and the button is disabled.
- **The grade is deterministic.** The legacy query read from an unordered join and
  let the loop overwrite, so whichever exam row SQL happened to return last decided
  the grade. The most recent exam is used here.
- **Every subject counts.** `convertgrade` totalled subjects 1 and 2 only. Bands are
  unchanged: 95 AA, 90 A+, 80 A, 75 B+, 60 B, 50 C, below that D.
- **`isComplete` is surfaced.** The legacy page issued the certificate whether or not
  the course was finished. That behaviour is kept — the certificate is still issued —
  but it is marked *Provisional* and the page warns first.
- **No html2pdf.** The legacy certificate pulled html2pdf from a CDN and rasterised
  the page at scale 3, producing a large blurry bitmap. Both downloads open a print
  window, where the browser's own *Save as PDF* keeps the text selectable.

### The combined "Receptionist & Teacher" role

Five live accounts hold `Receptionist & Teacher` in a single `Roles` column.

**Those five could never sign in to the legacy system.** `AccountController.Login`
tested `user.Roles == "Admin"`, `== "Student"`, `== "Teacher"`, `== "Receptionist"` by
exact string equality; the combined value matched no branch and fell through to
`else` → *"Invalid login attempt."* Even had it got past that, the roles string went
into the auth ticket whole, so `[Authorize(Roles = "Teacher")]` would not have matched
it either.

`UserRoles.Split` breaks the value on `&`, `,`, `/` and `+`, and the JWT carries one
role claim per role, so the account works and receives the **union** of both roles.
Verified end to end with such an account:

| Endpoint | Result |
|---|---|
| `reports/teacher-earnings` (own) | 200, pinned to their own teacher record |
| `reports/teacher-earnings?teacherId=<other>` | 403 |
| `reports/my-teacher-id` | 200 |
| `courses/progress?courseId=7` | 200 (Receptionist half) |
| `courses/progress` unfiltered | 403 (still withheld) |
| `courses/lookup` | 200 (feeds their filter) |
| courses master, marks, notices, expenses, students, users, income, dashboard | 403 |
| `reports/student-statement` | 403 |

`Primary()` ranks Admin > Editor > Receptionist > Teacher > Student, so the landing
page is `/courses`. The Angular sidebar filters on `roles.some(r => held.includes(r))`,
so the account sees About Us, Courses and Teacher Account and nothing else.

One cosmetic wart, recorded because the name misleads: the JWT carries a `loginId`
claim taken from `Users.LoginId`, but in the legacy schema that column holds **the
email of whoever created the record**, not the user's own login — user 1 has
`nargis@gmail.com`, and users 2-5 all carry the admin who created them. The claim is
written and never read; identity comes from the email claim.
