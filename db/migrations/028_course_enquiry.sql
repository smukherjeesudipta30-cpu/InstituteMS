/*  028_course_enquiry.sql
    ----------------------
    The walk-in enquiry register.

    Someone comes to the counter and asks about a course. The receptionist takes
    their name, the course, a phone number and an address, and the institute can
    then chase it. Until now that conversation left no trace, so an enquiry that
    did not convert on the day was simply lost.

    NOT the same thing as dbo.CusMessage, which is the "Send Us Message" form on
    the public website. That one arrives by itself and is answered; this one is
    typed by a person at the desk and is followed up. They are kept apart on
    purpose - merging them would mean one list where half the rows have no phone
    number and the other half have no message.

    Idempotent: safe to run more than once.
*/

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CourseEnquiry' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.CourseEnquiry
    (
        Id              INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_CourseEnquiry PRIMARY KEY,

        /*  What was taken at the counter.  */
        StudentName     NVARCHAR(200) NOT NULL,
        Phone           VARCHAR(20)   NOT NULL,
        Address         NVARCHAR(500) NULL,

        /*  The course asked about. Nullable and NOT a foreign key on purpose:
            people ask about things vaguely ("something with computers"), and a
            course that is later removed must not take the enquiry with it. The
            name is resolved through dbo.Course at read time.  */
        CourseId        INT           NULL,

        /*  Free text for anything that does not fit a column, and for what was
            said on each follow-up call.  */
        Remarks         NVARCHAR(1000) NULL,

        EnquiryDate     DATE          NOT NULL
            CONSTRAINT DF_CourseEnquiry_EnquiryDate DEFAULT (CAST(GETDATE() AS DATE)),

        /*  'New', 'Following', 'Converted', 'NotInterested'. A short varchar
            rather than a lookup table: four values that the office reads directly
            out of a report do not need a join.  */
        [Status]        VARCHAR(20)   NOT NULL
            CONSTRAINT DF_CourseEnquiry_Status DEFAULT ('New'),

        /*  The follow-up mechanics. NextFollowUpOn is what the report is built
            around - "who am I supposed to ring today".  */
        NextFollowUpOn  DATE          NULL,
        LastFollowUpOn  DATE          NULL,
        FollowUpCount   INT           NOT NULL
            CONSTRAINT DF_CourseEnquiry_FollowUpCount DEFAULT (0),

        /*  Which branch took the enquiry, matching the rest of the application.  */
        BranchId        INT           NULL,

        /*  Set when the enquiry becomes an admission, so the report can show what
            the follow-up actually produced.  */
        ConvertedStudentRegistrationId VARCHAR(50) NULL,

        CreatedBy       VARCHAR(250)  NULL,
        CreatedOn       DATETIME      NOT NULL
            CONSTRAINT DF_CourseEnquiry_CreatedOn DEFAULT (GETDATE()),
        ModifiedBy      VARCHAR(250)  NULL,
        ModifiedOn      DATETIME      NULL
    );

    /*  The follow-up list: open enquiries due on or before today, oldest first.  */
    CREATE INDEX IX_CourseEnquiry_FollowUp
        ON dbo.CourseEnquiry ([Status], NextFollowUpOn);

    /*  The register itself, and the date-range report.  */
    CREATE INDEX IX_CourseEnquiry_EnquiryDate
        ON dbo.CourseEnquiry (EnquiryDate DESC);

    /*  Finding a caller again by the number they rang from.  */
    CREATE INDEX IX_CourseEnquiry_Phone
        ON dbo.CourseEnquiry (Phone);

    PRINT 'Created dbo.CourseEnquiry.';
END
ELSE
BEGIN
    PRINT 'dbo.CourseEnquiry already exists - nothing to do.';
END
GO
