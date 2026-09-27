-- Holiday list.
--
-- One row per holiday per year. Category is National, State (the West Bengal
-- government list), Festival (fetched from the Indian holiday calendar) or
-- Institute (a day Admin adds). The institute does not close on every
-- government holiday, so IsInstituteHoliday says whether it is actually closed
-- that day. Any year other than 2026 is fetched automatically the first time
-- someone opens it; Admin then maintains each year from the Holidays page.
--
-- Seeded with the West Bengal government holidays for 2026 (Finance (Audit)
-- Department notification of 27 Nov 2025, https://wb.gov.in/pdf/Holiday-2026.pdf),
-- plus Kali Puja on Sunday 8 Nov. National days and the main festivals start
-- as institute-closed; the "additional" days and smaller observances start open.
-- Admin should review these once and set them to what the institute follows.
--
-- Idempotent: safe to run more than once, and never overwrites a row Admin has edited.

IF OBJECT_ID('dbo.Holiday') IS NULL
BEGIN
    CREATE TABLE dbo.Holiday (
        Id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Holiday PRIMARY KEY,
        HolidayDate        DATE           NOT NULL,
        Name               NVARCHAR(150)  NOT NULL,
        Category           VARCHAR(20)    NOT NULL CONSTRAINT DF_Holiday_Category DEFAULT 'Institute',
        IsInstituteHoliday BIT            NOT NULL CONSTRAINT DF_Holiday_IsInstituteHoliday DEFAULT 1,
        Note               NVARCHAR(300)  NULL,
        CreatedBy          VARCHAR(250)   NULL,
        CreatedOn          DATETIME2      NOT NULL CONSTRAINT DF_Holiday_CreatedOn DEFAULT SYSDATETIME(),
        ModifiedBy         VARCHAR(250)   NULL,
        ModifiedOn         DATETIME2      NULL,
        CONSTRAINT CK_Holiday_Category CHECK (Category IN ('National', 'State', 'Festival', 'Institute'))
    );

    CREATE UNIQUE INDEX UX_Holiday_Date_Name ON dbo.Holiday (HolidayDate, Name);
END
GO

-- A database that ran an earlier draft of this script has the three-value check.
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = 'CK_Holiday_Category'
             AND definition NOT LIKE '%Festival%')
BEGIN
    ALTER TABLE dbo.Holiday DROP CONSTRAINT CK_Holiday_Category;
    ALTER TABLE dbo.Holiday ADD CONSTRAINT CK_Holiday_Category
        CHECK (Category IN ('National', 'State', 'Festival', 'Institute'));
END
GO

-- One row per year whose list has been loaded, so a year is fetched from the
-- holiday calendar only the first time anyone opens it, and Admin's later
-- edits and deletions are never undone by a re-fetch.
IF OBJECT_ID('dbo.HolidayYearImport') IS NULL
    CREATE TABLE dbo.HolidayYearImport (
        [Year]     INT           NOT NULL CONSTRAINT PK_HolidayYearImport PRIMARY KEY,
        Source     NVARCHAR(100) NOT NULL,
        Added      INT           NOT NULL CONSTRAINT DF_HolidayYearImport_Added DEFAULT 0,
        ImportedOn DATETIME2     NOT NULL CONSTRAINT DF_HolidayYearImport_ImportedOn DEFAULT SYSDATETIME(),
        ImportedBy VARCHAR(250)  NULL
    );
GO

;WITH seed (HolidayDate, Name, Category, IsInstituteHoliday) AS (
    SELECT CAST(d AS DATE), n, c, o FROM (VALUES
        ('2026-01-01', N'New Year''s Day',                                'State',    1),
        ('2026-01-12', N'Birthday of Swami Vivekananda',                  'State',    0),
        ('2026-01-22', N'Day prior to Saraswati Puja',                    'State',    0),
        ('2026-01-23', N'Netaji Subhas Chandra Bose Jayanti / Saraswati Puja', 'State', 1),
        ('2026-01-26', N'Republic Day',                                   'National', 1),
        ('2026-02-04', N'Shab-e-Barat',                                   'State',    0),
        ('2026-02-14', N'Birthday of Thakur Panchanan Barma',             'State',    0),
        ('2026-03-03', N'Doljatra',                                       'State',    1),
        ('2026-03-04', N'Holi',                                           'State',    1),
        ('2026-03-17', N'Birth Anniversary of Shri Shri Harichand Thakur','State',    0),
        ('2026-03-20', N'Day before Eid-ul-Fitr',                         'State',    0),
        ('2026-03-21', N'Eid-ul-Fitr',                                    'State',    1),
        ('2026-03-26', N'Ram Navami',                                     'State',    0),
        ('2026-03-31', N'Mahavir Jayanti',                                'State',    0),
        ('2026-04-03', N'Good Friday',                                    'State',    1),
        ('2026-04-14', N'Birthday of Dr. B. R. Ambedkar',                 'State',    0),
        ('2026-04-15', N'Bengali New Year (Poila Baisakh)',               'State',    1),
        ('2026-05-01', N'May Day / Buddha Purnima',                       'State',    0),
        ('2026-05-09', N'Rabindranath Tagore Jayanti',                    'State',    1),
        ('2026-05-26', N'Day before Id-ud-Zoha (Bakrid)',                 'State',    0),
        ('2026-05-27', N'Id-ud-Zoha (Bakrid)',                            'State',    1),
        ('2026-06-26', N'Muharram',                                       'State',    1),
        ('2026-07-16', N'Rathayatra',                                     'State',    0),
        ('2026-08-15', N'Independence Day',                               'National', 1),
        ('2026-08-26', N'Fateha-Dwaz-Daham',                              'State',    0),
        ('2026-08-28', N'Raksha Bandhan',                                 'State',    0),
        ('2026-09-04', N'Janmashtami',                                    'State',    1),
        ('2026-09-17', N'Vishwakarma Puja',                               'State',    0),
        ('2026-10-02', N'Gandhi Jayanti',                                 'National', 1),
        ('2026-10-10', N'Mahalaya',                                       'State',    1),
        ('2026-10-15', N'Durga Puja (Maha Chaturthi)',                    'State',    0),
        ('2026-10-16', N'Durga Puja (Maha Panchami)',                     'State',    0),
        ('2026-10-17', N'Durga Puja (Maha Shashthi)',                     'State',    1),
        ('2026-10-19', N'Durga Puja (Maha Ashtami)',                      'State',    1),
        ('2026-10-20', N'Durga Puja (Maha Nabami)',                       'State',    1),
        ('2026-10-21', N'Durga Puja (Vijaya Dashami)',                    'State',    1),
        ('2026-10-22', N'Additional Durga Puja holiday',                  'State',    0),
        ('2026-10-23', N'Additional Durga Puja holiday',                  'State',    0),
        ('2026-10-24', N'Additional Durga Puja holiday',                  'State',    0),
        ('2026-10-26', N'Additional Lakshmi Puja holiday',                'State',    0),
        ('2026-11-08', N'Kali Puja / Diwali',                             'State',    1),
        ('2026-11-09', N'Additional Kali Puja holiday',                   'State',    0),
        ('2026-11-10', N'Additional Kali Puja holiday',                   'State',    0),
        ('2026-11-11', N'Bhratridwitiya (Bhai Phonta)',                   'State',    1),
        ('2026-11-12', N'Day after Bhratridwitiya',                       'State',    0),
        ('2026-11-16', N'Chhath Puja',                                    'State',    0),
        ('2026-11-24', N'Guru Nanak Jayanti',                             'State',    0),
        ('2026-12-25', N'Christmas Day',                                  'State',    1)
    ) v (d, n, c, o)
)
INSERT INTO dbo.Holiday (HolidayDate, Name, Category, IsInstituteHoliday, CreatedBy)
SELECT s.HolidayDate, s.Name, s.Category, s.IsInstituteHoliday, 'system (WB govt list 2026)'
FROM seed s
WHERE NOT EXISTS (SELECT 1 FROM dbo.HolidayYearImport i WHERE i.[Year] = 2026)
  AND NOT EXISTS (SELECT 1 FROM dbo.Holiday h WHERE h.HolidayDate = s.HolidayDate AND h.Name = s.Name);

-- 2026 is loaded from the notification above, so it is never fetched from the calendar.
IF NOT EXISTS (SELECT 1 FROM dbo.HolidayYearImport WHERE [Year] = 2026)
    INSERT INTO dbo.HolidayYearImport ([Year], Source, Added, ImportedBy)
    VALUES (2026, N'WB govt notification', 48, 'system');
GO
