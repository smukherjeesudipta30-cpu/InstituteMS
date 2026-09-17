SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    045_competition_draft_registration.sql
    -------------------------------------------------------------------------
    Save-and-resume registration for the competition portal.

    "Register now" on the advertisement used to open straight into the full
    form -- name, documents, payment, all in one sitting. The owner asked for a
    quick-start step in front of it instead: a name, a phone and a password the
    visitor chooses themselves, which immediately gives them a registration id
    and lets them come back later -- on a different day, a different phone --
    and carry on where they left off.

    A draft row here reserves nothing. Dance capacity is still checked and
    consumed only inside CompetitionService.RegisterAsync's transaction, the
    same as before this migration -- an abandoned draft costs the group
    nothing and never blocks a real family from a seat.

    RUN WITH -f 65001 not required (ASCII only), but harmless either way.
*/

IF OBJECT_ID('dbo.CompetitionDraft', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompetitionDraft
    (
        Id               int IDENTITY(1,1) NOT NULL,
        CompetitionId    int NOT NULL,

        /* Filled in from the row's own identity right after the insert, the same
           way BillNo is derived on MonthlyFee -- never MAX(id)+1, which drifts the
           moment anything disturbs the identity counter. */
        RegistrationCode nvarchar(30) NOT NULL,

        ParticipantName  nvarchar(200) NOT NULL,
        Phone            nvarchar(15)  NOT NULL,
        PasswordHash     nvarchar(200) NOT NULL,

        /* 'Draft' until the visitor finishes the full form; 'Completed' once it is
           linked to a real CompetitionApplicant row. */
        Status           nvarchar(20)  NOT NULL CONSTRAINT DF_CompetitionDraft_Status DEFAULT (N'Draft'),
        LinkedApplicantId int NULL,

        CreatedOn        datetime2     NOT NULL CONSTRAINT DF_CompetitionDraft_CreatedOn DEFAULT (SYSDATETIME()),
        LastAccessedOn   datetime2     NULL,

        CONSTRAINT PK_CompetitionDraft PRIMARY KEY (Id),
        CONSTRAINT FK_CompetitionDraft_Competition
            FOREIGN KEY (CompetitionId) REFERENCES dbo.Competition(Id),
        CONSTRAINT FK_CompetitionDraft_Applicant
            FOREIGN KEY (LinkedApplicantId) REFERENCES dbo.CompetitionApplicant(Id),
        CONSTRAINT CK_CompetitionDraft_Status CHECK (Status IN (N'Draft', N'Completed'))
    );

    CREATE UNIQUE INDEX UQ_CompetitionDraft_RegistrationCode
        ON dbo.CompetitionDraft(RegistrationCode);

    PRINT 'Created dbo.CompetitionDraft.';
END
GO

/* The dashboard notification this feature relies on -- AdminNotification already
   exists (migration 027) and is generic enough to carry this without a schema
   change: Title, StudentName (repurposed as the participant's name), RegistrationId
   (the draft code), LoginId (the phone), DetailValue (the password, in clear --
   the same precedent StudentPasswordChange already set, so the office can read it
   back to someone who calls in having forgotten it). No migration needed for that
   part; noted here so the two pieces of this feature are documented together. */
