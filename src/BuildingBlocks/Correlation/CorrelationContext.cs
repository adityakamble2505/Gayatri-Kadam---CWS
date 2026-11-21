namespace GreenCarWash.BuildingBlocks.Correlation;

public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? Id
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}
