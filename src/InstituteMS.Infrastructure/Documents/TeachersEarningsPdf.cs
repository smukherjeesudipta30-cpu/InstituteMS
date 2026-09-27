using System.Globalization;
using InstituteMS.Application.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace InstituteMS.Infrastructure.Documents;

/// <summary>
/// The teacher earnings report as a PDF: one summary table of every selected
/// teacher for the range, then each teacher's own section - the fee shares and
/// referral credits they earned, any missed-scan-out deduction, and what was
/// paid to them.
/// </summary>
/// <remarks>
/// Built on the server rather than printed from the browser, so the file is
/// the same on every device and a long list breaks across A4 pages cleanly,
/// with the table header repeated on each page.
/// </remarks>
public static class TeachersEarningsPdf
{
    private const string Ink = "#111827";
    private const string Muted = "#6b7280";
    private const string Rule = "#d1d5db";
    private const string Shade = "#f3f4f6";

    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    public static byte[] Build(TeachersEarningsReportDto report)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var range = $"{report.From:dd MMM yyyy} to {report.To:dd MMM yyyy}";

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4.Portrait());
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontSize(8.5f).FontColor(Ink).FontFamily(Fonts.Arial));

                page.Header().PaddingBottom(8).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("AVIJAN COMPUTER CENTRE").FontSize(13).Bold();
                            c.Item().Text("Teacher Earnings Report").FontSize(10).SemiBold();
                        });
                        row.ConstantItem(200).AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text(range).SemiBold();
                            c.Item().AlignRight().Text($"{report.Teachers.Count} teacher(s)").FontColor(Muted);
                        });
                    });
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Ink);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Text("Summary").FontSize(10).Bold();
                    col.Item().Element(c => SummaryTable(c, report));

                    foreach (var t in report.Teachers)
                        col.Item().Element(c => TeacherSection(c, t));
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text($"Generated {report.GeneratedOn:dd MMM yyyy, hh:mm tt}").FontSize(7).FontColor(Muted);
                    row.RelativeItem().AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(7).FontColor(Muted));
                        t.Span("Page ");
                        t.CurrentPageNumber();
                        t.Span(" of ");
                        t.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void SummaryTable(IContainer container, TeachersEarningsReportDto report)
    {
        var deductionsUsed = report.TotalDeduction != 0m;

        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(22);
                c.RelativeColumn(3);
                c.RelativeColumn(1.5f);
                if (deductionsUsed) c.RelativeColumn(1.5f);
                c.RelativeColumn(1.5f);
                c.RelativeColumn(1.5f);
            });

            table.Header(h =>
            {
                HeadCell(h.Cell(), "#");
                HeadCell(h.Cell(), "Teacher");
                HeadCell(h.Cell(), "Earned", right: true);
                if (deductionsUsed) HeadCell(h.Cell(), "Deduction", right: true);
                HeadCell(h.Cell(), "Paid", right: true);
                HeadCell(h.Cell(), "Balance", right: true);
            });

            var n = 0;
            foreach (var t in report.Teachers)
            {
                n++;
                BodyCell(table.Cell(), n.ToString(CultureInfo.InvariantCulture));
                BodyCell(table.Cell(), t.TeacherName ?? $"Teacher #{t.TeacherId}");
                BodyCell(table.Cell(), Money(t.TotalEarned), right: true);
                if (deductionsUsed) BodyCell(table.Cell(), Money(t.TotalDeduction), right: true);
                BodyCell(table.Cell(), Money(t.TotalPaid), right: true);
                BodyCell(table.Cell(), Money(t.Pending), right: true, bold: true);
            }

            TotalCell(table.Cell(), "");
            TotalCell(table.Cell(), "Total");
            TotalCell(table.Cell(), Money(report.TotalEarned), right: true);
            if (deductionsUsed) TotalCell(table.Cell(), Money(report.TotalDeduction), right: true);
            TotalCell(table.Cell(), Money(report.TotalPaid), right: true);
            TotalCell(table.Cell(), Money(report.Pending), right: true);
        });
    }

    private static void TeacherSection(IContainer container, TeacherEarningsReportDto t)
    {
        container.PaddingTop(6).Column(col =>
        {
            col.Spacing(4);

            col.Item().Background(Shade).Padding(5).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span(t.TeacherName ?? $"Teacher #{t.TeacherId}").FontSize(10).Bold();
                    if (!string.IsNullOrWhiteSpace(t.TeacherPhone))
                        text.Span($"   {t.TeacherPhone}").FontColor(Muted);
                });
                row.AutoItem().AlignRight().Text(
                    $"Earned {Money(t.TotalEarned)}" +
                    (t.TotalDeduction != 0m ? $"  ·  Deduction {Money(t.TotalDeduction)}" : "") +
                    $"  ·  Paid {Money(t.TotalPaid)}  ·  Balance {Money(t.Pending)}").SemiBold();
            });

            if (t.Earnings.Count == 0)
            {
                col.Item().Text("No earnings in this period.").FontColor(Muted).Italic();
            }
            else
            {
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(62);
                        c.RelativeColumn(2.2f);
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(2.6f);
                        c.ConstantColumn(62);
                    });

                    table.Header(h =>
                    {
                        HeadCell(h.Cell(), "Date");
                        HeadCell(h.Cell(), "Student");
                        HeadCell(h.Cell(), "Reg. no.");
                        HeadCell(h.Cell(), "Course / source");
                        HeadCell(h.Cell(), "Amount", right: true);
                    });

                    foreach (var e in t.Earnings.OrderBy(e => e.PaidOn))
                    {
                        BodyCell(table.Cell(), e.PaidOn?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "—");
                        BodyCell(table.Cell(), e.StudentName ?? "—");
                        BodyCell(table.Cell(), e.RegistrationId ?? "—");
                        BodyCell(table.Cell(), e.CourseName ?? "—");
                        BodyCell(table.Cell(), Money(e.Salary), right: true);
                    }
                });
            }

            foreach (var d in t.Deductions)
                col.Item().Text($"Deduction: {d.Reason}  ({Money(d.DeductionAmount)})").FontColor(Muted);

            if (t.Payments.Count > 0)
            {
                col.Item().Text(text =>
                {
                    text.Span("Paid: ").SemiBold();
                    text.Span(string.Join(",  ", t.Payments.OrderBy(p => p.PaidOn).Select(p =>
                        $"{p.PaidOn?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "—"} {Money(p.Amount)}")));
                });
            }
        });
    }

    private static void HeadCell(IContainer cell, string text, bool right = false)
    {
        var c = cell.Background(Shade).BorderBottom(1).BorderColor(Ink).PaddingVertical(3).PaddingHorizontal(4);
        (right ? c.AlignRight() : c).Text(text).SemiBold();
    }

    private static void BodyCell(IContainer cell, string text, bool right = false, bool bold = false)
    {
        var c = cell.BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(2.5f).PaddingHorizontal(4);
        var t = (right ? c.AlignRight() : c).Text(text);
        if (bold) t.SemiBold();
    }

    private static void TotalCell(IContainer cell, string text, bool right = false)
    {
        var c = cell.BorderTop(1).BorderColor(Ink).Background(Shade).PaddingVertical(3).PaddingHorizontal(4);
        (right ? c.AlignRight() : c).Text(text).Bold();
    }

    private static string Money(decimal amount) => "₹" + amount.ToString("#,##0.00", India);
}
