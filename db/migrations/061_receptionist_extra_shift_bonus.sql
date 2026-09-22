-- Kept separate from ReceptionistPayment.Amount deliberately: Amount is the
-- standard monthly pay, which changes only when an Admin deliberately changes
-- the payment structure. An extra-shift bonus folded into Amount would inflate
-- next month's per-shift rate (itself derived from Amount), compounding every
-- time someone covered a colleague's shift.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.ReceptionistPayment') AND name = 'ExtraShiftBonus'
)
    ALTER TABLE dbo.ReceptionistPayment
        ADD ExtraShiftBonus DECIMAL(18,2) NOT NULL
            CONSTRAINT DF_ReceptionistPayment_ExtraShiftBonus DEFAULT 0;
