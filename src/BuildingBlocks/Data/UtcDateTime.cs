using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GreenCarWash.BuildingBlocks.Data;

public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(static value => AsUtc(value), static value => AsUtc(value))
    {
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

public sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter()
        : base(
            static value => value.HasValue ? AsUtc(value.Value) : value,
            static value => value.HasValue ? AsUtc(value.Value) : value)
    {
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
