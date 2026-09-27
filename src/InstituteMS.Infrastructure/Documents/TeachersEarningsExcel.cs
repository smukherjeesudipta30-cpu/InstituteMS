using ClosedXML.Excel;
using InstituteMS.Application.Reports;

namespace InstituteMS.Infrastructure.Documents;

/// <summary>
/// The teacher earnings report as an Excel workbook: a Summary sheet (one row
/// per teacher, with totals) and an Earnings sheet (every earning line, with
/// the teacher's name, so it can be filtered and sorted in Excel). Same
/// figures as the PDF and the screen.
/// </summary>
public static class TeachersEarningsExcel
{
    private const string MoneyFormat = "#,##0.00";
    private const string DateFormat = "dd-mmm-yyyy";

    public static byte[] Build(TeachersEarningsReportDto report)
    {
        using var book = new XLWorkbook();

        Summary(book.Worksheets.Add("Summary"), report);
        Earnings(book.Worksheets.Add("Earnings"), report);

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Summary(IXLWorksheet sheet, TeachersEarningsReportDto report)
    {
        sheet.Cell(1, 1).Value = "Avijan Computer Centre - Teacher Earnings";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13);
        sheet.Cell(2, 1).Value = $"{report.From:dd MMM yyyy} to {report.To:dd MMM yyyy}";

        string[] headers = ["#", "Teacher", "Phone", "Earned", "Deduction", "Net earned", "Paid", "Balance"];
        const int headRow = 4;
        for (var c = 0; c < headers.Length; c++) sheet.Cell(headRow, c + 1).Value = headers[c];
        Header(sheet.Range(headRow, 1, headRow, headers.Length));

        var row = headRow;
        var n = 0;
        foreach (var t in report.Teachers)
        {
            row++;
            n++;
            sheet.Cell(row, 1).Value = n;
            sheet.Cell(row, 2).Value = t.TeacherName ?? $"Teacher #{t.TeacherId}";
            sheet.Cell(row, 3).Value = t.TeacherPhone ?? "";
            sheet.Cell(row, 4).Value = t.TotalEarned;
            sheet.Cell(row, 5).Value = t.TotalDeduction;
            sheet.Cell(row, 6).Value = t.NetEarned;
            sheet.Cell(row, 7).Value = t.TotalPaid;
            sheet.Cell(row, 8).Value = t.Pending;
        }

        row++;
        sheet.Cell(row, 2).Value = "Total";
        sheet.Cell(row, 4).Value = report.TotalEarned;
        sheet.Cell(row, 5).Value = report.TotalDeduction;
        sheet.Cell(row, 6).Value = report.NetEarned;
        sheet.Cell(row, 7).Value = report.TotalPaid;
        sheet.Cell(row, 8).Value = report.Pending;
        sheet.Range(row, 1, row, headers.Length).Style.Font.SetBold()
            .Border.SetTopBorder(XLBorderStyleValues.Thin);

        sheet.Range(headRow + 1, 4, row, 8).Style.NumberFormat.Format = MoneyFormat;
        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(headRow);
    }

    private static void Earnings(IXLWorksheet sheet, TeachersEarningsReportDto report)
    {
        string[] headers = ["Teacher", "Date", "Student", "Reg. no.", "Course / source", "Amount"];
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
        Header(sheet.Range(1, 1, 1, headers.Length));

        var row = 1;
        foreach (var t in report.Teachers)
        {
            foreach (var e in t.Earnings.OrderBy(e => e.PaidOn))
            {
                row++;
                sheet.Cell(row, 1).Value = t.TeacherName ?? $"Teacher #{t.TeacherId}";
                if (e.PaidOn is { } d) sheet.Cell(row, 2).Value = d.ToDateTime(TimeOnly.MinValue);
                sheet.Cell(row, 3).Value = e.StudentName ?? "";
                sheet.Cell(row, 4).Value = e.RegistrationId ?? "";
                sheet.Cell(row, 5).Value = e.CourseName ?? "";
                sheet.Cell(row, 6).Value = e.Salary;
            }
        }

        if (row > 1)
        {
            sheet.Range(2, 2, row, 2).Style.NumberFormat.Format = DateFormat;
            sheet.Range(2, 6, row, 6).Style.NumberFormat.Format = MoneyFormat;
            sheet.Range(1, 1, row, headers.Length).SetAutoFilter();
        }

        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);
    }

    private static void Header(IXLRange range) =>
        range.Style.Font.SetBold()
            .Fill.SetBackgroundColor(XLColor.FromHtml("#f3f4f6"))
            .Border.SetBottomBorder(XLBorderStyleValues.Thin);
}
