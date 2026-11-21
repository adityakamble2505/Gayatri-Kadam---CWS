using Microsoft.Data.SqlClient;

namespace GreenCarWash.BuildingBlocks.Data;

public static class SqlServerErrors
{
    public static bool IsDuplicateKey(Exception exception) => HasNumber(exception, 2601, 2627);

    public static bool IsDeadlock(Exception exception) => HasNumber(exception, 1205);

    public static async Task ExecuteOnceAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (IsDeadlock(exception))
        {
            await action();
        }
    }

    private static bool HasNumber(Exception exception, params int[] numbers)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && Array.IndexOf(numbers, sql.Number) >= 0)
                return true;
        }

        return false;
    }
}
