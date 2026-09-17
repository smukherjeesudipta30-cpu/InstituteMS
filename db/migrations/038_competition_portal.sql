SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    038_competition_portal.sql
    -------------------------------------------------------------------------
    The yearly Drawing & Dance competition, open to the public.

    Five tables, and none of them touch dbo.student or dbo.Users. The owner was
    explicit: "This will be for anonymus - no relationship with existing student
    or user". Entrants are children from any school, most of them once in their
    lives; giving each one a sign-in would put hundreds of rows in the same list
    the centre's own staff and students live in, for no gain. A registration
    number and the guardian's phone is how an entry is found again.

    Shape of it:

      Competition          one row per year -- fees, times, rules, open/close
      CompetitionGroup     the divisions and groups, with dance reporting times
      CompetitionOrganiser the names shown on the form
      CompetitionApplicant one submission: the person, the documents, the payment
      CompetitionEntry     one line per competition entered, each with its own
                           registration number
      CompetitionDocument  photograph, school id, birth certificate, payment slip

    An applicant is separate from an entry because a child may enter both
    competitions on one form and one payment of Rs 140, while the two are judged
    separately and each needs its own number.

    Seeded for 2027. The 2026 event on the posters has already been held, and the
    owner asked for "next year onwards".

    RUN THIS WITH -f 65001.

        sqlcmd -S .\SQLEXPRESS -d Avijan -E -C -b -f 65001 -i 038_competition_portal.sql

    This file is UTF-8 and carries Bengali. Without -f 65001 sqlcmd reads it in the
    system ANSI codepage, and every N'...' literal below is silently mangled on the
    way in -- the rows insert without error and the Bengali comes back as Latin-1
    rubbish. It was caught here by checking UNICODE(LEFT(LabelBengali,1)): Bengali
    lives at 2432-2559, and the first pass had stored 224.
*/

/* ==================================================== the year's settings */

IF OBJECT_ID('dbo.Competition') IS NULL
BEGIN
    CREATE TABLE dbo.Competition
    (
        Id                  int IDENTITY(1,1) NOT NULL,
        [Year]              int              NOT NULL,
        EventDate           date             NULL,
        Venue               nvarchar(200)    NULL,
        VenueBengali        nvarchar(200)    NULL,

        /* Rupees. Admin-editable, because the owner said the yearly amount may
           change. */
        DrawingFee          decimal(10,2)    NOT NULL CONSTRAINT DF_Competition_DrawingFee DEFAULT (40),
        DanceFee            decimal(10,2)    NOT NULL CONSTRAINT DF_Competition_DanceFee   DEFAULT (100),

        DrawingStartTime    time(0)          NULL,
        DanceStartTime      time(0)          NULL,
        PrizeGivingTime     time(0)          NULL,

        /* Both languages, because the advertisement is shown in both. */
        RulesEnglish        nvarchar(max)    NULL,
        RulesBengali        nvarchar(max)    NULL,
        NoticeEnglish       nvarchar(max)    NULL,
        NoticeBengali       nvarchar(max)    NULL,

        /* Outside these dates the landing page shows nothing at all, so an old
           event cannot advertise itself after the fact. */
        RegistrationOpensOn date             NULL,
        RegistrationClosesOn date            NULL,

        ContactPhones       nvarchar(200)    NULL,
        IsActive            bit              NOT NULL CONSTRAINT DF_Competition_IsActive DEFAULT (1),

        CreatedBy           nvarchar(50)     NULL,
        CreatedOn           datetime         NULL,
        ModifiedBy          nvarchar(50)     NULL,
        ModifiedOn          datetime         NULL,

        CONSTRAINT PK_Competition PRIMARY KEY (Id),
        CONSTRAINT UQ_Competition_Year UNIQUE ([Year])
    );

    PRINT 'Created dbo.Competition.';
END
GO

/* ============================================ divisions and dance groups */

IF OBJECT_ID('dbo.CompetitionGroup') IS NULL
BEGIN
    CREATE TABLE dbo.CompetitionGroup
    (
        Id              int IDENTITY(1,1) NOT NULL,
        CompetitionId   int              NOT NULL,

        /* 'Drawing' or 'Dance'. */
        Kind            nvarchar(10)     NOT NULL,

        Code            nvarchar(20)     NOT NULL,
        LabelEnglish    nvarchar(120)    NOT NULL,
        LabelBengali    nvarchar(120)    NULL,

        /* Dance is decided by age, drawing by class. Whichever pair is filled in
           is the one the portal matches on; both may be null for a division the
           entrant simply picks. */
        MinAge          int              NULL,
        MaxAge          int              NULL,
        MinClass        int              NULL,
        MaxClass        int              NULL,

        /* Dance only: when this group reports on the day. The owner asked that
           batch timing be set by the admin for dance, and said plainly that
           drawing needs no batch -- a registration number is enough. */
        ReportingTime   time(0)          NULL,

        SortOrder       int              NOT NULL CONSTRAINT DF_CompetitionGroup_Sort DEFAULT (0),

        CONSTRAINT PK_CompetitionGroup PRIMARY KEY (Id),
        CONSTRAINT FK_CompetitionGroup_Competition
            FOREIGN KEY (CompetitionId) REFERENCES dbo.Competition (Id),
        CONSTRAINT CK_CompetitionGroup_Kind CHECK (Kind IN ('Drawing', 'Dance'))
    );

    CREATE INDEX IX_CompetitionGroup_Competition
        ON dbo.CompetitionGroup (CompetitionId, Kind, SortOrder);

    PRINT 'Created dbo.CompetitionGroup.';
END
GO

/* ================================================= names shown on the form */

IF OBJECT_ID('dbo.CompetitionOrganiser') IS NULL
BEGIN
    CREATE TABLE dbo.CompetitionOrganiser
    (
        Id            int IDENTITY(1,1) NOT NULL,
        CompetitionId int              NOT NULL,
        [Name]        nvarchar(150)    NOT NULL,
        Designation   nvarchar(150)    NULL,
        Phone         nvarchar(20)     NULL,
        SortOrder     int              NOT NULL CONSTRAINT DF_CompetitionOrganiser_Sort DEFAULT (0),
        IsActive      bit              NOT NULL CONSTRAINT DF_CompetitionOrganiser_Active DEFAULT (1),

        CONSTRAINT PK_CompetitionOrganiser PRIMARY KEY (Id),
        CONSTRAINT FK_CompetitionOrganiser_Competition
            FOREIGN KEY (CompetitionId) REFERENCES dbo.Competition (Id)
    );

    PRINT 'Created dbo.CompetitionOrganiser.';
END
GO

/* ==================================================== one submitted form */

IF OBJECT_ID('dbo.CompetitionApplicant') IS NULL
BEGIN
    CREATE TABLE dbo.CompetitionApplicant
    (
        Id              int IDENTITY(1,1) NOT NULL,
        CompetitionId   int              NOT NULL,

        ParticipantName nvarchar(150)    NOT NULL,
        GuardianName    nvarchar(150)    NULL,
        DateOfBirth     date             NULL,
        SchoolName      nvarchar(200)    NULL,
        ClassName       nvarchar(50)     NULL,
        Phone           nvarchar(20)     NOT NULL,
        Email           nvarchar(150)    NULL,
        [Address]       nvarchar(400)    NULL,

        /* 'Online' or 'Venue'. Venue is refused for dance in the application:
           the owner requires online payment for dance, and allows paying on
           arrival only for drawing. */
        PaymentMode     nvarchar(10)     NOT NULL,
        Amount          decimal(10,2)    NOT NULL,

        /*
            The UPI reference the payer types in.

            Nothing here can ask the bank whether that payment happened -- the QR
            is static and no gateway reports back. What the unique index buys is
            the one check that IS possible and that matters: the same reference
            cannot be submitted by two different entrants. Confirmation itself is
            an administrator pressing a button after looking at the account, the
            same arrangement the student online fee payments already use.
        */
        TransactionRef  nvarchar(64)     NULL,

        /* 'Pending', 'Confirmed', 'AtVenue', 'Rejected'. */
        PaymentStatus   nvarchar(12)     NOT NULL CONSTRAINT DF_CompetitionApplicant_Status DEFAULT ('Pending'),
        ConfirmedBy     nvarchar(50)     NULL,
        ConfirmedOn     datetime         NULL,

        SubmittedOn     datetime         NOT NULL CONSTRAINT DF_CompetitionApplicant_On DEFAULT (GETDATE()),
        SourceIp        nvarchar(45)     NULL,

        CONSTRAINT PK_CompetitionApplicant PRIMARY KEY (Id),
        CONSTRAINT FK_CompetitionApplicant_Competition
            FOREIGN KEY (CompetitionId) REFERENCES dbo.Competition (Id),
        CONSTRAINT CK_CompetitionApplicant_Mode CHECK (PaymentMode IN ('Online', 'Venue')),
        CONSTRAINT CK_CompetitionApplicant_Status
            CHECK (PaymentStatus IN ('Pending', 'Confirmed', 'AtVenue', 'Rejected'))
    );

    CREATE INDEX IX_CompetitionApplicant_Competition
        ON dbo.CompetitionApplicant (CompetitionId, PaymentStatus);

    CREATE INDEX IX_CompetitionApplicant_Phone
        ON dbo.CompetitionApplicant (Phone);

    /* One UPI reference, one entrant. Filtered so the many venue-payers, who have
       no reference at all, do not collide with each other on NULL. */
    CREATE UNIQUE INDEX UQ_CompetitionApplicant_TransactionRef
        ON dbo.CompetitionApplicant (TransactionRef)
        WHERE TransactionRef IS NOT NULL;

    PRINT 'Created dbo.CompetitionApplicant.';
END
GO

/* ============================== one line per competition actually entered */

IF OBJECT_ID('dbo.CompetitionEntry') IS NULL
BEGIN
    CREATE TABLE dbo.CompetitionEntry
    (
        Id             int IDENTITY(1,1) NOT NULL,
        ApplicantId    int              NOT NULL,
        CompetitionId  int              NOT NULL,
        Kind           nvarchar(10)     NOT NULL,
        GroupId        int              NULL,

        /* AVI/DRW/2027/001. Serial is kept beside it so the next number is a
           MAX on an integer rather than string arithmetic on the formatted one. */
        Serial         int              NOT NULL,
        RegistrationNo nvarchar(40)     NOT NULL,

        CreatedOn      datetime         NOT NULL CONSTRAINT DF_CompetitionEntry_On DEFAULT (GETDATE()),

        CONSTRAINT PK_CompetitionEntry PRIMARY KEY (Id),
        CONSTRAINT FK_CompetitionEntry_Applicant
            FOREIGN KEY (ApplicantId) REFERENCES dbo.CompetitionApplicant (Id),
        CONSTRAINT FK_CompetitionEntry_Competition
            FOREIGN KEY (CompetitionId) REFERENCES dbo.Competition (Id),
        CONSTRAINT FK_CompetitionEntry_Group
            FOREIGN KEY (GroupId) REFERENCES dbo.CompetitionGroup (Id),
        CONSTRAINT CK_CompetitionEntry_Kind CHECK (Kind IN ('Drawing', 'Dance')),

        /* Two numbers of the same kind in the same year can never be issued
           twice, whatever happens above this table. */
        CONSTRAINT UQ_CompetitionEntry_Serial UNIQUE (CompetitionId, Kind, Serial),
        CONSTRAINT UQ_CompetitionEntry_Number UNIQUE (RegistrationNo),

        /* And nobody enters the same competition twice on one form. */
        CONSTRAINT UQ_CompetitionEntry_Once UNIQUE (ApplicantId, Kind)
    );

    PRINT 'Created dbo.CompetitionEntry.';
END
GO

/* ================================================ photographs and papers */

IF OBJECT_ID('dbo.CompetitionDocument') IS NULL
BEGIN
    CREATE TABLE dbo.CompetitionDocument
    (
        Id          int IDENTITY(1,1) NOT NULL,
        ApplicantId int              NOT NULL,

        /* 'Photo', 'SchoolId', 'BirthCertificate', 'PaymentSlip'. */
        Kind        nvarchar(20)     NOT NULL,

        FileName    nvarchar(200)    NULL,
        ContentType nvarchar(100)    NULL,

        /* Inline Base64, as student and staff photographs already are, so a
           document travels with the database backup rather than living on a file
           share that has to be backed up and restored in step. */
        Content     nvarchar(max)    NOT NULL,

        UploadedOn  datetime         NOT NULL CONSTRAINT DF_CompetitionDocument_On DEFAULT (GETDATE()),

        CONSTRAINT PK_CompetitionDocument PRIMARY KEY (Id),
        CONSTRAINT FK_CompetitionDocument_Applicant
            FOREIGN KEY (ApplicantId) REFERENCES dbo.CompetitionApplicant (Id),
        CONSTRAINT CK_CompetitionDocument_Kind
            CHECK (Kind IN ('Photo', 'SchoolId', 'BirthCertificate', 'PaymentSlip')),
        CONSTRAINT UQ_CompetitionDocument_One UNIQUE (ApplicantId, Kind)
    );

    PRINT 'Created dbo.CompetitionDocument.';
END
GO

/* ================================================================= seed */

IF NOT EXISTS (SELECT 1 FROM dbo.Competition WHERE [Year] = 2027)
BEGIN
    INSERT dbo.Competition
        ([Year], EventDate, Venue, VenueBengali,
         DrawingFee, DanceFee,
         DrawingStartTime, DanceStartTime, PrizeGivingTime,
         RegistrationOpensOn, RegistrationClosesOn,
         ContactPhones, IsActive, CreatedBy, CreatedOn,
         NoticeEnglish, NoticeBengali, RulesEnglish, RulesBengali)
    VALUES
        (2027, NULL, 'Utpal Dutta Mancha, Rajpur Banstala, Kolkata 700149',
         N'রাজপুর উৎপল দত্ত মঞ্চ',
         40, 100,
         '09:00', '12:00', '17:30',
         NULL, NULL,
         '7688045949 / 6290891537 / 9875666848', 1, 'migration-038', GETDATE(),
         N'Drawing & Dance Competition organised by Avijan Computer Centre.',
         N'অভিযান কম্পিউটার সেন্টার আয়োজিত বসে আঁকো ও নৃত্য প্রতিযোগিতা।',
         N'• Competitors must reach the venue at least 30 minutes before the competition starts.' + CHAR(13) + CHAR(10) +
         N'• Bring your own drawing materials. Paper is supplied by the institute.' + CHAR(13) + CHAR(10) +
         N'• Pen, sketch pen and scale may not be used.' + CHAR(13) + CHAR(10) +
         N'• A dance performance must be less than 2 minutes.' + CHAR(13) + CHAR(10) +
         N'• The judges'' decision is final and binding.' + CHAR(13) + CHAR(10) +
         N'• Dance only: 40% of the registration fee is refunded in cash at the venue on the programme day.' + CHAR(13) + CHAR(10) +
         N'• If a participant is absent on the day, the fee is not refunded.' + CHAR(13) + CHAR(10) +
         N'• Birth certificate or age proof may be asked for.',
         N'• প্রতিযোগীকে প্রতিযোগিতা শুরু হওয়ার অন্তত ৩০ মিনিট আগে প্রতিযোগিতা স্থানে উপস্থিত থাকতে হবে।' + CHAR(13) + CHAR(10) +
         N'• আঁকার সরঞ্জাম নিজের সঙ্গে আনতে হবে। আঁকার কাগজ প্রতিষ্ঠান থেকে দেওয়া হবে।' + CHAR(13) + CHAR(10) +
         N'• কোনো রকম পেন, স্কেচ পেন, স্কেল ব্যবহার করা যাবে না।' + CHAR(13) + CHAR(10) +
         N'• নৃত্যের সময়সীমা ২ মিনিটের কম হতে হবে।' + CHAR(13) + CHAR(10) +
         N'• বিচারকের সিদ্ধান্তই চূড়ান্ত বলে মান্য হবে।' + CHAR(13) + CHAR(10) +
         N'• শুধুমাত্র নৃত্যের ক্ষেত্রে: অনুষ্ঠানের দিন অনুষ্ঠানস্থলেই রেজিস্ট্রেশন ফি-এর ৪০% নগদে ফেরত দেওয়া হবে।' + CHAR(13) + CHAR(10) +
         N'• প্রতিযোগী অনুপস্থিত থাকলে কোনো টাকা ফেরত দেওয়া হবে না।' + CHAR(13) + CHAR(10) +
         N'• প্রয়োজনে জন্ম শংসাপত্র বা বয়সের প্রমাণ চাওয়া হতে পারে।');

    PRINT 'Seeded the 2027 competition.';
END
GO

DECLARE @cid int = (SELECT Id FROM dbo.Competition WHERE [Year] = 2027);

IF @cid IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.CompetitionGroup WHERE CompetitionId = @cid)
BEGIN
    /* Drawing divisions, by class, exactly as the Bengali poster prints them. */
    INSERT dbo.CompetitionGroup
        (CompetitionId, Kind, Code, LabelEnglish, LabelBengali, MinClass, MaxClass, SortOrder)
    VALUES
        (@cid, 'Drawing', N'ক', N'Nursery to Class I',   N'বিভাগ – ক (নার্সারী থেকে প্রথম শ্রেণী)',      0,  1, 1),
        (@cid, 'Drawing', N'খ', N'Class II to IV',       N'বিভাগ – খ (দ্বিতীয় শ্রেণী থেকে চতুর্থ শ্রেণী)', 2,  4, 2),
        (@cid, 'Drawing', N'গ', N'Class V to VIII',      N'বিভাগ – গ (পঞ্চম শ্রেণী থেকে অষ্টম শ্রেণী)',    5,  8, 3),
        (@cid, 'Drawing', N'ঘ', N'Class IX to XII',      N'বিভাগ – ঘ (নবম শ্রেণী থেকে দ্বাদশ শ্রেণী)',     9, 12, 4);

    /* Dance groups, by age. Reporting times start empty: the owner sets them. */
    INSERT dbo.CompetitionGroup
        (CompetitionId, Kind, Code, LabelEnglish, LabelBengali, MinAge, MaxAge, SortOrder)
    VALUES
        (@cid, 'Dance', 'A', 'Group A · Age 5 to 8',    N'গ্রুপ এ · বয়স ৫ থেকে ৮',   5,   8, 1),
        (@cid, 'Dance', 'B', 'Group B · Age 9 to 14',   N'গ্রুপ বি · বয়স ৯ থেকে ১৪',  9,  14, 2),
        (@cid, 'Dance', 'C', 'Group C · Age 15 and above', N'গ্রুপ সি · বয়স ১৫ ও তদূর্ধ্বে', 15, 200, 3);

    PRINT 'Seeded the drawing divisions and dance groups.';
END

IF @cid IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.CompetitionOrganiser WHERE CompetitionId = @cid)
BEGIN
    INSERT dbo.CompetitionOrganiser (CompetitionId, [Name], Designation, SortOrder)
    VALUES
        (@cid, N'Anindita Mukherjee', N'Owner', 1),
        (@cid, N'Nargis Khatun', N'Head Receptionist & HR', 2),
        (@cid, N'Arkita Nath', N'Entertainment & Programme Section', 3);

    PRINT 'Seeded the organiser names.';
END
GO

SELECT competitions = (SELECT COUNT(*) FROM dbo.Competition),
       groups       = (SELECT COUNT(*) FROM dbo.CompetitionGroup),
       organisers   = (SELECT COUNT(*) FROM dbo.CompetitionOrganiser);
GO
