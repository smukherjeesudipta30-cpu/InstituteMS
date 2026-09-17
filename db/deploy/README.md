# Production deployment

Both applications run against the **same live database**, `ph19987796653_`.
GoDaddy will not allow a second database on the account, so the new Angular 19
/ .NET 8 application is being brought to the legacy data rather than the data
being copied to it. The legacy application keeps running throughout.

Run these two scripts, in this order, against `ph19987796653_`.

## 1. `LEGACY_COEXIST_UPGRADE.sql`

Adds everything the new application needs. **Touches nothing the legacy
application uses** — no column dropped, renamed, retyped, widened, narrowed or
made NOT NULL, no row deleted.

That rule is stricter than "only add columns", and it has to be. The legacy
application has seven `INSERT INTO <table> VALUES (...)` statements with no
column list — `CusMessage`, `Course`, `Expense`, `TecExpense`, `Notice`,
`OIncome`, `Teacher`. Such an insert must supply a value for every non-identity
column and a DEFAULT does not excuse it, so adding a single column to any of
those seven would break recording an expense, adding a course, saving a notice
and creating a teacher. Those tables are never altered; `Expense.Branch` and
`OIncome.Branch` live in the new side tables `ExpenseExt` / `OIncomeExt`
instead, keyed by the legacy id.

Also kept deliberately: `Users.Password` (dropping it would end sign-in for all
754 accounts — `PasswordHash` is added beside it) and `Branch.Branch_name` /
`Branch_id` / `Is_active`. No foreign key points at a legacy table.

```bash
sqlcmd -S <server> -d ph19987796653_ -U <user> -P <password> -I -b -i LEGACY_COEXIST_UPGRADE.sql -o coexist_log.txt
```

## 2. `MONEY_PRECISION_UPGRADE.sql`

Gives the twelve money columns two decimal places, `decimal(18,0)` →
`decimal(18,2)`, so amounts stop rounding paise away. Precision is read from
each column and carried through; only the scale moves.

This one **does** alter live columns, which is why it is separate. The visible
effect on the legacy application is cosmetic: amounts render as `100.00`
instead of `100` on screens, reports and printed receipts, because every legacy
read is `sdr["Amount"].ToString()` into a string property. Nothing was found to
break — no `Convert.ToInt32` on a money field, no string comparison of an
amount, and JavaScript money goes through `parseFloat`, never `parseInt`.

Historical rounding is not recovered: an amount already stored as `101` stays
`101.00`.

`ALTER COLUMN` rewrites each table under a schema-modification lock. At these
volumes it is sub-second, but it blocks the legacy application while it runs —
**run it when nobody is using the system.**

```bash
sqlcmd -S <server> -d ph19987796653_ -U <user> -P <password> -I -b -i MONEY_PRECISION_UPGRADE.sql -o money_log.txt
```

## Running either script

Take a backup first. For the money script the backup is the rollback.

In SSMS: select the database and turn on **SQLCMD Mode** (Query → SQLCMD Mode)
before executing. Without it the `:on error exit` line is a syntax error and
SSMS carries on past a failed preflight.

Both scripts are idempotent — running twice is harmless — and both refuse to
run against anything that is not the legacy schema.

## Still required before the new application will start

The scripts are necessary but not sufficient. The API's EF Core model still
expects the migrated shapes and has to be remapped onto the legacy ones:
`datetime` rather than `date`, the legacy `Branch` column names, the
`ExpenseExt` / `OIncomeExt` side tables, and no audit columns on the seven
tables above. That is C# work, tracked separately.

Configuration, not SQL:

1. **`JWT__KEY`** — at least 32 characters. The API refuses to start without it.
2. **`ConnectionStrings:DefaultConnection`** — point at `ph19987796653_`.
3. **UPI details** for the fee-payment QR code.
4. **Branches** — the coexistence script seeds `Rajpur`. Add others on the
   Branches screen; expenses, other income and receptionists all require one.
5. **Roles** are a text column on `dbo.Users` (`Roles`), so there is no role
   table to seed. Assign `Receptionist` on the Users screen.

## Testing the new application against live data

The new application **writes** to the live database. Exercising admissions,
fees and exams creates real rows the legacy application will then display.
Start with the read-only screens — reports, statements, dashboards — before
anything that saves.

## A note on the local mirror

`ph19987796653_prod` is a snapshot built by `migrations/011` with
`SELECT ... INTO`, which silently drops `IDENTITY`. Production has identity on
`Expense.id`, `OIncome.ID`, `CusMessage.id`, `Course.CId`, `Notice.NID`,
`Teacher.id`, `TecExpense.id` and `Branch.id`; the mirror has none of them.
Rebuild those tables before using the mirror to test anything that depends on
identity, defaults or constraints.
