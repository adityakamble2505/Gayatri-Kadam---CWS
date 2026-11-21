using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GreenCarWash.BuildingBlocks.Data;

public sealed class UtcUpdatedAtInterceptor : SaveChangesInterceptor
{
    private readonly IClock _clock;

    public UtcUpdatedAtInterceptor(IClock clock)
    {
        _clock = clock;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
            return;

        var now = _clock.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            var updatedAt = entry.Properties.FirstOrDefault(property => property.Metadata.Name == "UpdatedAt");
            if (updatedAt is not null)
                updatedAt.CurrentValue = now;
        }
    }
}
