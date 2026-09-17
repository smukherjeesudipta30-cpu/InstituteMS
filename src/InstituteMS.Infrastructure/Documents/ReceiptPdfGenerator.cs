using System.Globalization;
using InstituteMS.Application.Documents;
using InstituteMS.Application.Fees;
using InstituteMS.Application.Students;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace InstituteMS.Infrastructure.Documents;

/// <summary>
/// The admission and money receipts as PDF, laid out like the printed ones: the
/// Avijan mark, the AVIJAN masthead with the bill and registration numbers either
/// side, the address, then the details boxed off.
/// </summary>
/// <remarks>
/// <para>QuestPDF under its Community licence, which is free for organisations
/// below one million US dollars of annual revenue. The licence is declared in the
/// constructor because QuestPDF refuses to render until it is.</para>
///
/// <para>The logo is read from <c>wwwroot/images</c> once and cached: it is the
/// same file on every receipt, and re-reading it per document would put a disk read
/// in the path of every admission and every fee taken.</para>
/// </remarks>
public class ReceiptPdfGenerator : IReceiptPdfGenerator
{
    private const string Ink = "#212529";
    private const string Muted = "#6B7280";
    private const string Line = "#B0B4BA";
    private const string Wash = "#F3F4F6";

    private const string ComputerGenerated =
        "This is a computer generated bill. No signature is required.";

    private readonly byte[]? _logo;
    private readonly ILogger<ReceiptPdfGenerator> _logger;

    public ReceiptPdfGenerator(ILogger<ReceiptPdfGenerator> logger)
    {
        _logger = logger;

        QuestPDF.Settings.License = LicenseType.Community;

        /* Read once. A missing file is not a reason to fail a receipt — the
           document simply prints without the mark, which is far better than an
           admission failing because an image moved.

           BaseDirectory rather than IHostEnvironment.ContentRootPath: this project
           does not reference the hosting abstractions, and taking a dependency on
           the whole hosting stack to find one file would be the wrong trade. In the
           published layout the binaries and wwwroot sit in the same folder, so the
           two are the same path. In development there is no wwwroot at all, and the
           warning below is the correct outcome. */
        var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "images", "logo.png");

        try
        {
            if (File.Exists(path)) _logo = File.ReadAllBytes(path);
            else _logger.LogWarning("Receipt logo not found at {Path}; PDFs will omit it.", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the receipt logo at {Path}.", path);
        }
    }

    /* ----------------------------------------------------------- documents */

    public byte[] AdmissionReceipt(AdmissionReceiptDto r) =>
        Build("ADMISSION RECEIPT", r.BillNo, r.RegistrationId, table =>
        {
            Row(table, "Name", r.Name);
            Row(table, "Course", r.CourseName);
            Row(table, "Date of Joining", ShortDate(r.DateOfJoining));
            Row(table, "Admission Fees", Rupees(r.AdmissionFees));
            Row(table, "In Words", Words(r.AdmissionFeesInWords));
            Row(table, "Next Payment Due", ShortDate(r.NextDueDate));
        });

    public byte[] MoneyReceipt(FeeReceiptDto r) =>
        Build("MONEY RECEIPT", r.BillNo, r.RegistrationId, table =>
        {
            Row(table, "Name", r.StudentName);
            Row(table, "Purpose", r.CourseName);
            Row(table, "Date", ShortDate(r.FeeDate));
            Row(table, "Amount Paid", Rupees(r.AmountPaid));
            Row(table, "In Words", Words(r.AmountPaidInWords));
            Row(table, "Total Dues Until Today", Rupees(r.NewDue));

            if (r.NextDueDate is not null)
                Row(table, "Next Payment Due", ShortDate(r.NextDueDate));
        });

    /* -------------------------------------------------------------- layout */

    private byte[] Build(
        string title, string? billNo, string? registrationId, Action<TableDescriptor> rows) =>
        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                /* A5 landscape is exactly half an A4 portrait sheet, so two receipts
                   print on one page and cut cleanly down the middle. Everything
                   below is sized to fit that height with room to spare: a receipt
                   that spills onto a second page cannot be handed over the counter. */
                page.Size(PageSizes.A5.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(t => t.FontSize(10).FontColor(Ink).FontFamily(Fonts.Arial));

                // ExtendVertical so the border frames the whole half-page rather than
                // shrinking to the text, the way a pre-printed receipt pad does.
                /* Decoration rather than one long column: its Content takes the
                   height that is left over and its After is laid out below that,
                   which puts the note at the foot of the box whatever the table
                   does. A spacer item cannot do this — an extending item inside a
                   column claims the full height and tips the note onto page two. */
                page.Content().ExtendVertical().Border(1).BorderColor(Ink).Padding(12)
                    .Decoration(deco =>
                {
                deco.Content().Column(col =>
                {
                    col.Spacing(7);

                    /* Masthead: bill number left, AVIJAN centred, registration
                       number right — the arrangement the paper receipt uses. */
                    col.Item().Row(row =>
                    {
                        row.RelativeItem(3).AlignLeft().AlignBottom()
                            .Text(t =>
                            {
                                t.Span("Bill No: ").SemiBold();
                                t.Span(billNo ?? "-");
                            });

                        row.RelativeItem(4).AlignCenter().Column(c =>
                        {
                            if (_logo is not null)
                                c.Item().AlignCenter().Height(22).Image(_logo);

                            c.Item().AlignCenter().Text("AVIJAN")
                                .FontSize(20).Bold().LetterSpacing(0.05f);
                        });

                        row.RelativeItem(3).AlignRight().AlignBottom()
                            .Text(t =>
                            {
                                t.Span("Reg No: ").SemiBold();
                                t.Span(registrationId ?? "-");
                            });
                    });

                    col.Item().AlignCenter().Text("AN UNIT OF COMPUTER PROFESSIONALS & LEARNERS")
                        .FontSize(8).SemiBold();
                    col.Item().AlignCenter().Text("Rajpur. Opp. Of Bangla School, Kolkata - 700149")
                        .FontSize(8).SemiBold();
                    col.Item().AlignCenter().Text("Mob.: 7688045949").FontSize(8).SemiBold();

                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Ink);

                    col.Item().AlignCenter().Text(title)
                        .FontSize(11).Bold().LetterSpacing(0.14f);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(3);
                        });

                        rows(table);
                    });

                });

                deco.After().PaddingTop(6).AlignCenter()
                    .Text(ComputerGenerated).FontSize(8).Italic().FontColor(Muted);
                });
            });
        }).GeneratePdf();

    /// <summary>One label/value line. Skipped entirely when there is no value.</summary>
    private static void Row(TableDescriptor table, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        table.Cell().Background(Wash).Border(1).BorderColor(Line).Padding(4)
            .Text(label).SemiBold();

        table.Cell().Border(1).BorderColor(Line).Padding(4)
            .Text(value);
    }

    /* ------------------------------------------------------------- helpers */

    /// <summary>Indian grouping, no symbol — as the printed receipts do it.</summary>
    private static string Rupees(decimal? value) =>
        (value ?? 0m).ToString("#,##0.00", CultureInfo.GetCultureInfo("en-IN"));

    private static string ShortDate(DateOnly? value) =>
        value?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "-";

    private static string? Words(string? words) =>
        string.IsNullOrWhiteSpace(words) ? null : $"{words} Only";
}
