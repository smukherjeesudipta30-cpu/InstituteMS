using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace InstituteMS.Infrastructure.Persistence;

/* =====================================================================
   Value converters that let the model sit on the legacy column types.

   The application runs against the live legacy database, alongside the
   ASP.NET MVC application, because the hosting account allows only one
   database. The legacy schema cannot be corrected in place - seven of
   its tables are written by INSERT statements with no column list, so
   altering them breaks the other application - so the model bends to
   the schema instead.

   NOT all of these are safe against both schemas. The DateOnly converters
   are - they read a datetime or a date equally well.
   DecimalOverTextConverter is NOT: it
   requires the column to be varchar, and throws "Unable to cast object
   of type System.Decimal to type System.String" against the migrated
   schema, where the same column is decimal. It is deliberately
   legacy-only, because the legacy database is the target.

   These are classes rather than static instances because
   ModelConfigurationBuilder.HaveConversion<T>() takes a converter TYPE.
   ===================================================================== */

/// <summary>
/// <c>DateOnly</c> over a <c>datetime</c> column.
/// </summary>
/// <remarks>
/// EF Core 8 maps <see cref="DateOnly"/> to <c>date</c>. The legacy columns are
/// <c>datetime</c>, so without this the provider has no mapping and the model
/// fails to build.
///
/// <para>Reading takes the date part and discards the time. That loses nothing
/// the application uses - it treats these as dates - but it is a real discard:
/// 581 <c>monthlyFees.PDate</c> rows carry a time of day, and it is not written
/// back. Writing always produces midnight, which is what the legacy application
/// already stores for most rows.</para>
///
/// <para>Against a genuine <c>date</c> column this is still correct; the round
/// trip is simply lossless there.</para>
/// </remarks>
public class DateOnlyToDateTimeConverter : ValueConverter<DateOnly, DateTime>
{
    public DateOnlyToDateTimeConverter()
        : base(d => d.ToDateTime(TimeOnly.MinValue),
               dt => DateOnly.FromDateTime(dt))
    {
    }
}

/// <inheritdoc cref="DateOnlyToDateTimeConverter"/>
public class NullableDateOnlyToDateTimeConverter : ValueConverter<DateOnly?, DateTime?>
{
    public NullableDateOnlyToDateTimeConverter()
        : base(d => d.HasValue ? d.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null,
               dt => dt.HasValue ? DateOnly.FromDateTime(dt.Value) : (DateOnly?)null)
    {
    }
}

/// <summary>
/// A <c>decimal?</c> over a <c>varchar</c> column holding a number as text.
/// </summary>
/// <remarks>
/// <c>student.Stu_monthly_fees</c> and <c>monthlyFees.stu_actual_monthly_fees</c>
/// are <c>varchar(250)</c> in the legacy schema. The deployment scripts leave
/// them as text deliberately: being text, they never rounded anything, so
/// converting them would be risk without benefit.
///
/// <para>Blank and unparseable text reads as NULL rather than throwing. A fee
/// somebody once typed as "n/a" should leave that one student's fee unknown,
/// not fail the whole page. Writing uses the invariant culture, so the stored
/// text never depends on the server's locale.</para>
/// </remarks>
public class DecimalOverTextConverter : ValueConverter<decimal?, string?>
{
    public DecimalOverTextConverter()
        : base(d => d.HasValue ? d.Value.ToString(CultureInfo.InvariantCulture) : null,
               s => Parse(s))
    {
    }

    private static decimal? Parse(string? text)
        => decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
