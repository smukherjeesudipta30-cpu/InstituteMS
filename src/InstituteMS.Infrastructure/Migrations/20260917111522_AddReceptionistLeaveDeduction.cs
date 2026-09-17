using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InstituteMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReceptionistLeaveDeduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminNotification",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    StudentName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CourseName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    RegistrationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LoginId = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    DetailValue = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReadBy = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminNotification", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdmissionFeePayment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    RegistrationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentMode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PaidOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsAtAdmission = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdmissionFeePayment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceChallenge",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaffType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StaffId = table.Column<int>(type: "int", nullable: false),
                    StaffName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    LoginId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MarkedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OnDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    AccuracyMetres = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    DistanceMetres = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    CodeHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SentToEmail = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ExpiresOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    ConsumedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceChallenge", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceShift",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    ScanOutBy = table.Column<TimeOnly>(type: "time", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceShift", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Branch",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BranchCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    GeofenceMetres = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Branch_name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Branch_id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Is_active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branch", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "CertificateIssue",
                columns: table => new
                {
                    IssueId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StuRegId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IssueNo = table.Column<int>(type: "int", nullable: false),
                    IssuedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IssuedTo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateIssue", x => x.IssueId);
                });

            migrationBuilder.CreateTable(
                name: "ChatConversation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastMessageOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StaffLastReadMessageId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConversation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatFcmSubscription",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Token = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatFcmSubscription", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatPushSubscription",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    P256dh = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatPushSubscription", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Competition",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    EventDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Venue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VenueBengali = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DrawingFee = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DanceFee = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DrawingStartTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    DanceStartTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    PrizeGivingTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    DrawingRulesEnglish = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DrawingRulesBengali = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DanceRulesEnglish = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DanceRulesBengali = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NoticeEnglish = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NoticeBengali = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RegistrationOpensOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RegistrationClosesOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ContactPhones = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Competition", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionDraft",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionId = table.Column<int>(type: "int", nullable: false),
                    RegistrationCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ParticipantName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LinkedApplicantId = table.Column<int>(type: "int", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastAccessedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PasswordResetCodeHash = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PasswordResetCodeExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PasswordResetAttempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionDraft", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Course",
                columns: table => new
                {
                    Cid = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Duration = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Course", x => x.Cid);
                });

            migrationBuilder.CreateTable(
                name: "CusMessage",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CusName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CusMailid = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CusPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    cusbody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CusMessage", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dailyRoutine",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    sun = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    mon = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    tues = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    wed = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    thurs = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    fri = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    sat = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dailyRoutine", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Expense",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExDetails = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expense", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Fees",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StuReg = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    StoreDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fees", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ImageUpload",
                columns: table => new
                {
                    FileID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StuRegId = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsInline = table.Column<bool>(type: "bit", nullable: false),
                    FileSize = table.Column<int>(type: "int", nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageUpload", x => x.FileID);
                });

            migrationBuilder.CreateTable(
                name: "LateFees",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StuRegId = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    BillNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FineFees = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DOD = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LateFees", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "MailLog_registration_time",
                columns: table => new
                {
                    MailLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    For_registraion_id = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ToEmail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MailBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailLog_registration_time", x => x.MailLogId);
                });

            migrationBuilder.CreateTable(
                name: "Marks",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RegId = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    ExDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Sub1 = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    M1 = table.Column<int>(type: "int", nullable: true),
                    OM1 = table.Column<int>(type: "int", nullable: true),
                    Sub2 = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    M2 = table.Column<int>(type: "int", nullable: true),
                    OM2 = table.Column<int>(type: "int", nullable: true),
                    Sub3 = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    M3 = table.Column<int>(type: "int", nullable: true),
                    OM3 = table.Column<int>(type: "int", nullable: true),
                    Sub4 = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    M4 = table.Column<int>(type: "int", nullable: true),
                    OM4 = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Marks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyFees",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StuRegId = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    BillNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Date1 = table.Column<DateTime>(type: "datetime2", nullable: true),
                    stu_actual_monthly_fees = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Installment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OInstallment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    percen = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OldDue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NewDue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TransactionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PaymentMode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VerifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyFees", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Notice",
                columns: table => new
                {
                    NID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NewNotice = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NoticeDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notice", x => x.NID);
                });

            migrationBuilder.CreateTable(
                name: "OIncome",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IPurpose = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ODate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OIncome", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "OrgNode",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Designation = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Qualification = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ComputerTraining = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsTechnical = table.Column<bool>(type: "bit", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    StaffType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    StaffId = table.Column<int>(type: "int", nullable: true),
                    IsDefaultStaffParent = table.Column<bool>(type: "bit", nullable: false),
                    PhotoFileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PhotoContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PhotoBase64 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgNode", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MissedScanOutDeductionEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Receptionist",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LoginId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PersonalEmail = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Designation = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Qualification = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ComputerTraining = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsTechnical = table.Column<bool>(type: "bit", nullable: false),
                    Branch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receptionist", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "StaffAttendance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaffType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StaffId = table.Column<int>(type: "int", nullable: false),
                    LoginId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StaffName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ScannedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OnDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    AccuracyMetres = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    DistanceMetres = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffAttendance", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teacher",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TecName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LoginId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TecPhone = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PersonalEmail = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Designation = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Qualification = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ComputerTraining = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsTechnical = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teacher", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Roles = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Loginid = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Phn_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    User_date_of_birth = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Registerdate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    End_date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    RefreshToken = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    RefreshTokenExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedLoginCount = table.Column<int>(type: "int", nullable: false),
                    LockoutEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PasswordResetCodeHash = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PasswordResetCodeExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PasswordResetAttempts = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "StaffShiftAssignment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaffType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StaffId = table.Column<int>(type: "int", nullable: false),
                    DayOfWeek = table.Column<byte>(type: "tinyint", nullable: false),
                    ShiftId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffShiftAssignment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffShiftAssignment_AttendanceShift_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "AttendanceShift",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessage",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationId = table.Column<int>(type: "int", nullable: false),
                    SenderEmail = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SenderDisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SenderRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SentOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AttachmentFileName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AttachmentContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AttachmentContent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttachmentSize = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessage_ChatConversation_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatParticipant",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationId = table.Column<int>(type: "int", nullable: false),
                    UserEmail = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    GuestToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Contact = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    JoinedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastReadMessageId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatParticipant", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatParticipant_ChatConversation_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionAdvert",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionId = table.Column<int>(type: "int", nullable: false),
                    Slot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    UploadedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UploadedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionAdvert", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionAdvert_Competition_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionApplicant",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionId = table.Column<int>(type: "int", nullable: false),
                    ParticipantName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    GuardianName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SchoolName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClassName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    PaymentMode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    TransactionRef = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PaymentStatus = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ConfirmedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ConfirmedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionApplicant", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionApplicant_Competition_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionGroup",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LabelEnglish = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    LabelBengali = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    MinAge = table.Column<int>(type: "int", nullable: true),
                    MaxAge = table.Column<int>(type: "int", nullable: true),
                    MinClass = table.Column<int>(type: "int", nullable: true),
                    MaxClass = table.Column<int>(type: "int", nullable: true),
                    Capacity = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionGroup", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionGroup_Competition_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionOrganiser",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompetitionId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Designation = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionOrganiser", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionOrganiser_Competition_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourseEnquiry",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CourseId = table.Column<int>(type: "int", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EnquiryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NextFollowUpOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastFollowUpOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FollowUpCount = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    ConvertedStudentRegistrationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseEnquiry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseEnquiry_Course_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Course",
                        principalColumn: "Cid");
                });

            migrationBuilder.CreateTable(
                name: "ExamPaper",
                columns: table => new
                {
                    ExamId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Cid = table.Column<int>(type: "int", nullable: true),
                    TotalMarks = table.Column<int>(type: "int", nullable: false),
                    PassMarks = table.Column<int>(type: "int", nullable: true),
                    DurationMins = table.Column<int>(type: "int", nullable: false),
                    OpensOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosesOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    PaperType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TermNumber = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamPaper", x => x.ExamId);
                    table.ForeignKey(
                        name: "FK_ExamPaper_Course_Cid",
                        column: x => x.Cid,
                        principalTable: "Course",
                        principalColumn: "Cid",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CusMessageExt",
                columns: table => new
                {
                    MessageId = table.Column<int>(type: "int", nullable: false),
                    IsHandled = table.Column<bool>(type: "bit", nullable: false),
                    HandledBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HandledOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CusMessageExt", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_CusMessageExt_CusMessage_MessageId",
                        column: x => x.MessageId,
                        principalTable: "CusMessage",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseExt",
                columns: table => new
                {
                    ExpenseId = table.Column<int>(type: "int", nullable: false),
                    Branch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseExt", x => x.ExpenseId);
                    table.ForeignKey(
                        name: "FK_ExpenseExt_Expense_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expense",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OIncomeExt",
                columns: table => new
                {
                    IncomeId = table.Column<int>(type: "int", nullable: false),
                    Branch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OIncomeExt", x => x.IncomeId);
                    table.ForeignKey(
                        name: "FK_OIncomeExt_OIncome_IncomeId",
                        column: x => x.IncomeId,
                        principalTable: "OIncome",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrgEdge",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentId = table.Column<int>(type: "int", nullable: false),
                    ChildId = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgEdge", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgEdge_OrgNode_ChildId",
                        column: x => x.ChildId,
                        principalTable: "OrgNode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrgEdge_OrgNode_ParentId",
                        column: x => x.ParentId,
                        principalTable: "OrgNode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceptionistLeaveDeduction",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceptionistId = table.Column<int>(type: "int", nullable: false),
                    PeriodType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EntitlementDays = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TakenDays = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    DeductionPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Applied = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceptionistLeaveDeduction", x => x.id);
                    table.ForeignKey(
                        name: "FK_ReceptionistLeaveDeduction_Receptionist_ReceptionistId",
                        column: x => x.ReceptionistId,
                        principalTable: "Receptionist",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReceptionistPayment",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceptionistId = table.Column<int>(type: "int", nullable: false),
                    PayMonth = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Branch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceptionistPayment", x => x.id);
                    table.ForeignKey(
                        name: "FK_ReceptionistPayment_Receptionist_ReceptionistId",
                        column: x => x.ReceptionistId,
                        principalTable: "Receptionist",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Student",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StuName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Stuaddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StuPhone = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    StuEmail = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StuRegId = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    BillNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NoTech = table.Column<int>(type: "int", nullable: true),
                    StuAdFees = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    AdmissionPaymentMode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    AdmissionFeesPaid = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Stu_monthly_fees = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DOJ = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DOB = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DOE = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Cid = table.Column<int>(type: "int", nullable: true),
                    TecName = table.Column<int>(type: "int", nullable: true),
                    TecName1 = table.Column<int>(type: "int", nullable: true),
                    TecName2 = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true),
                    IsComplete = table.Column<bool>(type: "bit", nullable: true),
                    loginId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Personal_mail_id = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Avijan_login_mail_id = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Avijan_login_Password = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Replica_id = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Student", x => x.id);
                    table.ForeignKey(
                        name: "FK_Student_Course_Cid",
                        column: x => x.Cid,
                        principalTable: "Course",
                        principalColumn: "Cid",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Student_Teacher_TecName",
                        column: x => x.TecName,
                        principalTable: "Teacher",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Student_Teacher_TecName1",
                        column: x => x.TecName1,
                        principalTable: "Teacher",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Student_Teacher_TecName2",
                        column: x => x.TecName2,
                        principalTable: "Teacher",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TecExpense",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TecId = table.Column<int>(type: "int", nullable: true),
                    InDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TecExpense", x => x.id);
                    table.ForeignKey(
                        name: "FK_TecExpense_Teacher_TecId",
                        column: x => x.TecId,
                        principalTable: "Teacher",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TecFees",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TecId = table.Column<int>(type: "int", nullable: true),
                    StuRegId = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    BillNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TecSal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TecFees", x => x.id);
                    table.ForeignKey(
                        name: "FK_TecFees_Teacher_TecId",
                        column: x => x.TecId,
                        principalTable: "Teacher",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionDocument",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicantId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionDocument", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionDocument_CompetitionApplicant_ApplicantId",
                        column: x => x.ApplicantId,
                        principalTable: "CompetitionApplicant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionEntry",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicantId = table.Column<int>(type: "int", nullable: false),
                    CompetitionId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: true),
                    Serial = table.Column<int>(type: "int", nullable: false),
                    RegistrationNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    BatchNo = table.Column<int>(type: "int", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionEntry_CompetitionApplicant_ApplicantId",
                        column: x => x.ApplicantId,
                        principalTable: "CompetitionApplicant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompetitionEntry_CompetitionGroup_GroupId",
                        column: x => x.GroupId,
                        principalTable: "CompetitionGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamAttempt",
                columns: table => new
                {
                    AttemptId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    StuRegId = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    StartedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Answers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Score = table.Column<int>(type: "int", nullable: true),
                    TotalMarks = table.Column<int>(type: "int", nullable: true),
                    CorrectCount = table.Column<int>(type: "int", nullable: true),
                    QuestionCount = table.Column<int>(type: "int", nullable: true),
                    AutoSubmitted = table.Column<bool>(type: "bit", nullable: false),
                    ViolationCount = table.Column<int>(type: "int", nullable: false),
                    Flagged = table.Column<bool>(type: "bit", nullable: false),
                    ShuffleSeed = table.Column<int>(type: "int", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    EnteredBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Cid = table.Column<int>(type: "int", nullable: true),
                    TermNumber = table.Column<int>(type: "int", nullable: true),
                    PaperType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamAttempt", x => x.AttemptId);
                    table.ForeignKey(
                        name: "FK_ExamAttempt_ExamPaper_ExamId",
                        column: x => x.ExamId,
                        principalTable: "ExamPaper",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamQuestion",
                columns: table => new
                {
                    QuestionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    QuestionNo = table.Column<int>(type: "int", nullable: false),
                    QuestionText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OptionA = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OptionB = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OptionC = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OptionD = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrectOption = table.Column<string>(type: "nvarchar(1)", nullable: false),
                    Marks = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamQuestion", x => x.QuestionId);
                    table.ForeignKey(
                        name: "FK_ExamQuestion_ExamPaper_ExamId",
                        column: x => x.ExamId,
                        principalTable: "ExamPaper",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamSchedule",
                columns: table => new
                {
                    ScheduleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    StuRegId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ScheduledOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AccessCode = table.Column<string>(type: "nchar(6)", fixedLength: true, maxLength: 6, nullable: true),
                    CodeIssuedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CodeIssuedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSchedule", x => x.ScheduleId);
                    table.ForeignKey(
                        name: "FK_ExamSchedule_ExamPaper_ExamId",
                        column: x => x.ExamId,
                        principalTable: "ExamPaper",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminNotification_ReadOn_CreatedOn",
                table: "AdminNotification",
                columns: new[] { "ReadOn", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_AdmissionFeePayment_StudentId",
                table: "AdmissionFeePayment",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceChallenge_Open",
                table: "AttendanceChallenge",
                columns: new[] { "StaffType", "StaffId", "ConsumedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceShift_Name",
                table: "AttendanceShift",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CertificateIssue_StuRegId_IssueNo",
                table: "CertificateIssue",
                columns: new[] { "StuRegId", "IssueNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatFcmSubscription_Email",
                table: "ChatFcmSubscription",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_ChatFcmSubscription_Token",
                table: "ChatFcmSubscription",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_ConversationId_Id",
                table: "ChatMessage",
                columns: new[] { "ConversationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatParticipant_ConversationId_UserEmail",
                table: "ChatParticipant",
                columns: new[] { "ConversationId", "UserEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatParticipant_GuestToken",
                table: "ChatParticipant",
                column: "GuestToken");

            migrationBuilder.CreateIndex(
                name: "IX_ChatPushSubscription_Email",
                table: "ChatPushSubscription",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_ChatPushSubscription_Endpoint",
                table: "ChatPushSubscription",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Competition_Year",
                table: "Competition",
                column: "Year",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionAdvert_CompetitionId_Slot",
                table: "CompetitionAdvert",
                columns: new[] { "CompetitionId", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionApplicant_CompetitionId",
                table: "CompetitionApplicant",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionDocument_ApplicantId",
                table: "CompetitionDocument",
                column: "ApplicantId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionDraft_RegistrationCode",
                table: "CompetitionDraft",
                column: "RegistrationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEntry_ApplicantId",
                table: "CompetitionEntry",
                column: "ApplicantId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEntry_CompetitionId_Kind_Serial",
                table: "CompetitionEntry",
                columns: new[] { "CompetitionId", "Kind", "Serial" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEntry_GroupId",
                table: "CompetitionEntry",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEntry_RegistrationNo",
                table: "CompetitionEntry",
                column: "RegistrationNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionGroup_CompetitionId",
                table: "CompetitionGroup",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionOrganiser_CompetitionId",
                table: "CompetitionOrganiser",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseEnquiry_CourseId",
                table: "CourseEnquiry",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseEnquiry_EnquiryDate",
                table: "CourseEnquiry",
                column: "EnquiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_CourseEnquiry_Phone",
                table: "CourseEnquiry",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "IX_CourseEnquiry_Status_NextFollowUpOn",
                table: "CourseEnquiry",
                columns: new[] { "Status", "NextFollowUpOn" });

            migrationBuilder.CreateIndex(
                name: "IX_CusMessage_SubDate",
                table: "CusMessage",
                column: "SubDate",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_CusMessageExt_IsHandled",
                table: "CusMessageExt",
                column: "IsHandled");

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempt_ExamId",
                table: "ExamAttempt",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamPaper_Cid",
                table: "ExamPaper",
                column: "Cid");

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestion_ExamId",
                table: "ExamQuestion",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSchedule_ExamId_StuRegId",
                table: "ExamSchedule",
                columns: new[] { "ExamId", "StuRegId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expense_ExDate",
                table: "Expense",
                column: "ExDate");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseExt_Branch",
                table: "ExpenseExt",
                column: "Branch");

            migrationBuilder.CreateIndex(
                name: "IX_Fees_StoreDate",
                table: "Fees",
                column: "StoreDate");

            migrationBuilder.CreateIndex(
                name: "IX_Fees_StuReg",
                table: "Fees",
                column: "StuReg");

            migrationBuilder.CreateIndex(
                name: "IX_ImageUpload_StuRegId",
                table: "ImageUpload",
                column: "StuRegId");

            migrationBuilder.CreateIndex(
                name: "IX_LateFees_DOD",
                table: "LateFees",
                column: "DOD");

            migrationBuilder.CreateIndex(
                name: "IX_LateFees_StuRegId",
                table: "LateFees",
                column: "StuRegId");

            migrationBuilder.CreateIndex(
                name: "IX_MailLog_registration_time_CreatedDate",
                table: "MailLog_registration_time",
                column: "CreatedDate",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Marks_RegId",
                table: "Marks",
                column: "RegId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyFees_Date1",
                table: "MonthlyFees",
                column: "Date1");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyFees_StuRegId",
                table: "MonthlyFees",
                column: "StuRegId");

            migrationBuilder.CreateIndex(
                name: "IX_OIncome_ODate",
                table: "OIncome",
                column: "ODate");

            migrationBuilder.CreateIndex(
                name: "IX_OIncomeExt_Branch",
                table: "OIncomeExt",
                column: "Branch");

            migrationBuilder.CreateIndex(
                name: "IX_OrgEdge_Child",
                table: "OrgEdge",
                column: "ChildId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgEdge_ParentId_ChildId",
                table: "OrgEdge",
                columns: new[] { "ParentId", "ChildId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceptionistLeaveDeduction_ReceptionistId_PeriodType_PeriodStart",
                table: "ReceptionistLeaveDeduction",
                columns: new[] { "ReceptionistId", "PeriodType", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceptionistPayment_ReceptionistId_PayMonth",
                table: "ReceptionistPayment",
                columns: new[] { "ReceptionistId", "PayMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendance_Date_Branch",
                table: "StaffAttendance",
                columns: new[] { "OnDate", "Branch" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendance_Staff_Date",
                table: "StaffAttendance",
                columns: new[] { "StaffType", "StaffId", "OnDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffShiftAssignment_ShiftId",
                table: "StaffShiftAssignment",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffShiftAssignment_Staff",
                table: "StaffShiftAssignment",
                columns: new[] { "StaffType", "StaffId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_Student_Cid",
                table: "Student",
                column: "Cid");

            migrationBuilder.CreateIndex(
                name: "IX_Student_StuRegId",
                table: "Student",
                column: "StuRegId");

            migrationBuilder.CreateIndex(
                name: "IX_Student_TecName",
                table: "Student",
                column: "TecName");

            migrationBuilder.CreateIndex(
                name: "IX_Student_TecName1",
                table: "Student",
                column: "TecName1");

            migrationBuilder.CreateIndex(
                name: "IX_Student_TecName2",
                table: "Student",
                column: "TecName2");

            migrationBuilder.CreateIndex(
                name: "IX_TecExpense_InDate",
                table: "TecExpense",
                column: "InDate");

            migrationBuilder.CreateIndex(
                name: "IX_TecExpense_TecId",
                table: "TecExpense",
                column: "TecId");

            migrationBuilder.CreateIndex(
                name: "IX_TecFees_TecId",
                table: "TecFees",
                column: "TecId");

            migrationBuilder.CreateIndex(
                name: "IX_TecFees_UpdateDate",
                table: "TecFees",
                column: "UpdateDate");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminNotification");

            migrationBuilder.DropTable(
                name: "AdmissionFeePayment");

            migrationBuilder.DropTable(
                name: "AttendanceChallenge");

            migrationBuilder.DropTable(
                name: "Branch");

            migrationBuilder.DropTable(
                name: "CertificateIssue");

            migrationBuilder.DropTable(
                name: "ChatFcmSubscription");

            migrationBuilder.DropTable(
                name: "ChatMessage");

            migrationBuilder.DropTable(
                name: "ChatParticipant");

            migrationBuilder.DropTable(
                name: "ChatPushSubscription");

            migrationBuilder.DropTable(
                name: "CompetitionAdvert");

            migrationBuilder.DropTable(
                name: "CompetitionDocument");

            migrationBuilder.DropTable(
                name: "CompetitionDraft");

            migrationBuilder.DropTable(
                name: "CompetitionEntry");

            migrationBuilder.DropTable(
                name: "CompetitionOrganiser");

            migrationBuilder.DropTable(
                name: "CourseEnquiry");

            migrationBuilder.DropTable(
                name: "CusMessageExt");

            migrationBuilder.DropTable(
                name: "dailyRoutine");

            migrationBuilder.DropTable(
                name: "ExamAttempt");

            migrationBuilder.DropTable(
                name: "ExamQuestion");

            migrationBuilder.DropTable(
                name: "ExamSchedule");

            migrationBuilder.DropTable(
                name: "ExpenseExt");

            migrationBuilder.DropTable(
                name: "Fees");

            migrationBuilder.DropTable(
                name: "ImageUpload");

            migrationBuilder.DropTable(
                name: "LateFees");

            migrationBuilder.DropTable(
                name: "MailLog_registration_time");

            migrationBuilder.DropTable(
                name: "Marks");

            migrationBuilder.DropTable(
                name: "MonthlyFees");

            migrationBuilder.DropTable(
                name: "Notice");

            migrationBuilder.DropTable(
                name: "OIncomeExt");

            migrationBuilder.DropTable(
                name: "OrgEdge");

            migrationBuilder.DropTable(
                name: "PayrollSettings");

            migrationBuilder.DropTable(
                name: "ReceptionistLeaveDeduction");

            migrationBuilder.DropTable(
                name: "ReceptionistPayment");

            migrationBuilder.DropTable(
                name: "StaffAttendance");

            migrationBuilder.DropTable(
                name: "StaffShiftAssignment");

            migrationBuilder.DropTable(
                name: "Student");

            migrationBuilder.DropTable(
                name: "TecExpense");

            migrationBuilder.DropTable(
                name: "TecFees");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "ChatConversation");

            migrationBuilder.DropTable(
                name: "CompetitionApplicant");

            migrationBuilder.DropTable(
                name: "CompetitionGroup");

            migrationBuilder.DropTable(
                name: "CusMessage");

            migrationBuilder.DropTable(
                name: "ExamPaper");

            migrationBuilder.DropTable(
                name: "Expense");

            migrationBuilder.DropTable(
                name: "OIncome");

            migrationBuilder.DropTable(
                name: "OrgNode");

            migrationBuilder.DropTable(
                name: "Receptionist");

            migrationBuilder.DropTable(
                name: "AttendanceShift");

            migrationBuilder.DropTable(
                name: "Teacher");

            migrationBuilder.DropTable(
                name: "Competition");

            migrationBuilder.DropTable(
                name: "Course");
        }
    }
}
