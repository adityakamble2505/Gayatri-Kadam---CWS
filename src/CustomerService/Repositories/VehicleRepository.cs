using GreenCarWash.CustomerService.Data;
using GreenCarWash.CustomerService.Entities;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.CustomerService.Repositories;

public interface IVehicleRepository
{
    Task<IReadOnlyList<Vehicle>> ListActiveAsync(int customerId, CancellationToken cancellationToken);

    Task<Vehicle?> FindActiveAsync(int customerId, int vehicleId, CancellationToken cancellationToken);

    Task<Vehicle?> FindActiveWithOwnerAsync(int customerId, int vehicleId, CancellationToken cancellationToken);

    Task<bool> NumberExistsAsync(int customerId, string vehicleNumber, CancellationToken cancellationToken);

    Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}

public sealed class VehicleRepository : IVehicleRepository
{
    private readonly CustomerDbContext _db;

    public VehicleRepository(CustomerDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Vehicle>> ListActiveAsync(int customerId, CancellationToken cancellationToken)
    {
        return await _db.Vehicles
            .AsNoTracking()
            .Where(vehicle => vehicle.CustomerId == customerId && vehicle.IsActive)
            .OrderBy(vehicle => vehicle.VehicleId)
            .ToListAsync(cancellationToken);
    }

    public Task<Vehicle?> FindActiveAsync(int customerId, int vehicleId, CancellationToken cancellationToken) =>
        _db.Vehicles.FirstOrDefaultAsync(
            vehicle => vehicle.VehicleId == vehicleId && vehicle.CustomerId == customerId && vehicle.IsActive,
            cancellationToken);

    public Task<Vehicle?> FindActiveWithOwnerAsync(int customerId, int vehicleId, CancellationToken cancellationToken) =>
        _db.Vehicles
            .Include(vehicle => vehicle.Customer)
            .FirstOrDefaultAsync(
                vehicle => vehicle.VehicleId == vehicleId && vehicle.CustomerId == customerId && vehicle.IsActive,
                cancellationToken);

    public Task<bool> NumberExistsAsync(int customerId, string vehicleNumber, CancellationToken cancellationToken) =>
        _db.Vehicles.AnyAsync(
            vehicle => vehicle.CustomerId == customerId && vehicle.VehicleNumber == vehicleNumber,
            cancellationToken);

    public async Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken) =>
        await _db.Vehicles.AddAsync(vehicle, cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
