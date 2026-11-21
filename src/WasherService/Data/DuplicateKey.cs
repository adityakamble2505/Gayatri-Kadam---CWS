using GreenCarWash.BuildingBlocks.Data;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.WasherService.Data;

internal static class DuplicateKey
{
    public static bool Is(DbUpdateException exception) => SqlServerErrors.IsDuplicateKey(exception);
}
