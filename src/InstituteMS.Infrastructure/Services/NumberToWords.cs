using System.Text;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Indian-numbering amount-in-words for printed receipts. Ports
/// <c>convertNumberToWords()</c> from <c>Scripts/Student.js</c>, which ran in the
/// browser; doing it server-side means the web receipt and the future APK receipt
/// read identically.
/// </summary>
public static class NumberToWords
{
    private static readonly string[] Ones =
    [
        "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
        "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen",
        "Seventeen", "Eighteen", "Nineteen"
    ];

    private static readonly string[] Tens =
    [
        "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
    ];

    public static string Convert(decimal amount)
    {
        if (amount == 0m) return "Zero";

        var negative = amount < 0m;
        amount = Math.Abs(amount);

        var rupees = (long)decimal.Truncate(amount);
        var paise = (int)Math.Round((amount - rupees) * 100m, MidpointRounding.AwayFromZero);

        // Rounding the fraction can carry into the rupee part (e.g. 10.999).
        if (paise == 100)
        {
            rupees++;
            paise = 0;
        }

        var words = new StringBuilder();

        if (negative) words.Append("Minus ");

        words.Append(rupees == 0 ? "Zero" : IndianGrouping(rupees));

        if (paise > 0)
        {
            words.Append(" and ").Append(IndianGrouping(paise)).Append(" Paise");
        }

        return words.ToString().Trim();
    }

    /// <summary>Crore / Lakh / Thousand / Hundred, as the legacy receipt printed them.</summary>
    private static string IndianGrouping(long value)
    {
        var parts = new List<string>();

        var crore = value / 10_000_000;
        value %= 10_000_000;

        var lakh = value / 100_000;
        value %= 100_000;

        var thousand = value / 1_000;
        value %= 1_000;

        var hundred = value / 100;
        var rest = value % 100;

        if (crore > 0) parts.Add($"{IndianGrouping(crore)} Crore");
        if (lakh > 0) parts.Add($"{TwoDigits((int)lakh)} Lakh");
        if (thousand > 0) parts.Add($"{TwoDigits((int)thousand)} Thousand");
        if (hundred > 0) parts.Add($"{Ones[hundred]} Hundred");
        if (rest > 0) parts.Add(TwoDigits((int)rest));

        return string.Join(" ", parts);
    }

    private static string TwoDigits(int value)
    {
        if (value < 20) return Ones[value];

        var tens = Tens[value / 10];
        var ones = value % 10;

        return ones == 0 ? tens : $"{tens} {Ones[ones]}";
    }
}
