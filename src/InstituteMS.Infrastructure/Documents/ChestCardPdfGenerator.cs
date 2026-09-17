using InstituteMS.Application.Competitions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace InstituteMS.Infrastructure.Documents;

/// <summary>
/// The chest number a dancer prints and pins on.
/// </summary>
/// <remarks>
/// <para>Issued as a PDF rather than left as a page to print, because it is fetched
/// on a phone: a browser print of a web page gives whatever the phone decides,
/// while a PDF arrives the same size on every device and can be sent to a shop to be
/// printed if the family has no printer.</para>
///
/// <para>The number is the whole point of the sheet, so it is set as large as the
/// page allows and in black — a chest number has to be readable from the judges'
/// table, and it must survive being printed on a machine with no colour ink.</para>
///
/// <para>The page is A6 — 105 by 148mm, a quarter of an A4 sheet cut in half twice —
/// the owner's own wording. Four print on one A4 page with no scaling, which is what
/// a shop with no A5/A6 paper of its own actually does with these.</para>
/// </remarks>
public class ChestCardPdfGenerator : IChestCardPdfGenerator
{
    private static readonly string Ink = "#111827";
    private static readonly string Muted = "#6b7280";

    public byte[] Build(ChestCardDto card)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A6.Portrait());
                page.Margin(12);
                page.DefaultTextStyle(t => t.FontSize(8).FontColor(Ink).FontFamily(Fonts.Arial));

                page.Content().Border(1.5f).BorderColor(Ink).Padding(10).Column(col =>
                {
                    col.Spacing(5);

                    col.Item().AlignCenter().Text("AVIJAN COMPUTER CENTRE")
                        .FontSize(10).SemiBold().LetterSpacing(0.03f);

                    col.Item().AlignCenter().Text($"Dance Competition {card.Year}")
                        .FontSize(8).FontColor(Muted);

                    col.Item().PaddingVertical(2).LineHorizontal(1).LineColor(Ink);

                    col.Item().AlignCenter().Text("CHEST NUMBER")
                        .FontSize(7).SemiBold().LetterSpacing(0.14f).FontColor(Muted);

                    /* The reason the sheet exists. As large as an A6 page can hold, and
                       black so a printer with no colour ink still produces it. */
                    col.Item().AlignCenter().Text(card.BatchNo.ToString())
                        .FontSize(80).Bold().LineHeight(1f);

                    col.Item().PaddingVertical(2).LineHorizontal(1).LineColor(Ink);

                    col.Item().Column(c =>
                    {
                        c.Item().Text("Participant").FontSize(6.5f).FontColor(Muted);
                        c.Item().Text(card.ParticipantName).FontSize(10).SemiBold();
                    });

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Group").FontSize(6.5f).FontColor(Muted);
                            c.Item().Text(card.GroupLabel ?? "-").FontSize(9).SemiBold();
                        });

                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("Date").FontSize(6.5f).FontColor(Muted);
                            c.Item().AlignRight()
                                .Text(card.EventDate?.ToString("d MMM yyyy") ?? "-")
                                .FontSize(9).SemiBold();
                        });
                    });

                    col.Item().Column(c =>
                    {
                        c.Item().Text("Registration number").FontSize(6.5f).FontColor(Muted);
                        c.Item().Text(card.RegistrationNo).FontSize(9).SemiBold();
                    });

                    if (!string.IsNullOrWhiteSpace(card.Venue))
                    {
                        col.Item().Column(c =>
                        {
                            c.Item().Text("Venue").FontSize(6.5f).FontColor(Muted);
                            c.Item().Text(card.Venue).FontSize(8);
                        });
                    }

                    col.Item().PaddingTop(3).Text(
                        "Print, cut along the border and pin to the chest before the " +
                        "performance. Bring it with you on the day.")
                        .FontSize(6.5f).FontColor(Muted);
                });
            });
        }).GeneratePdf();
    }
}
