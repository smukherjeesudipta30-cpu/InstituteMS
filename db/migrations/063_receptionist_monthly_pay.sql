-- The current standard monthly pay, set by the Admin on the receptionist fees
-- master screen. Separate from ReceptionistPayment.Amount, which is the actual
-- recorded transaction for one month -- this is only the default that month
-- starts from.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Receptionist') AND name = 'MonthlyPay'
)
    ALTER TABLE dbo.Receptionist
        ADD MonthlyPay DECIMAL(18, 2) NOT NULL
            CONSTRAINT DF_Receptionist_MonthlyPay DEFAULT 0;
