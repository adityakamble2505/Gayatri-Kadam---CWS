using FluentValidation;
using GreenCarWash.CatalogService.Data;
using GreenCarWash.CatalogService.DTOs;
using GreenCarWash.CatalogService.Entities;
using GreenCarWash.CatalogService.Repositories;
using GreenCarWash.BuildingBlocks.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.CatalogService.Services;

public interface IAddOnCatalog
{
    Task<IReadOnlyList<AddOnResponseDto>> ListActiveAsync(CancellationToken cancellationToken);

    Task<AddOnResponseDto> CreateAsync(AddOnWriteDto dto, CancellationToken cancellationToken);

    Task<AddOnResponseDto> UpdateAsync(int addOnId, AddOnWriteDto dto, CancellationToken cancellationToken);

    Task<AddOnResponseDto> SetActiveAsync(int addOnId, bool isActive, CancellationToken cancellationToken);
}

public sealed class AddOnCatalog : IAddOnCatalog
{
    private readonly IAddOnRepository _addOns;
    private readonly IValidator<AddOnWriteDto> _validator;
    private readonly ILogger<AddOnCatalog> _logger;

    public AddOnCatalog(IAddOnRepository addOns, IValidator<AddOnWriteDto> validator, ILogger<AddOnCatalog> logger)
    {
        _addOns = addOns;
        _validator = validator;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AddOnResponseDto>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var addOns = await _addOns.ListActiveAsync(cancellationToken);
        return addOns.Select(ToDto).ToArray();
    }

    public async Task<AddOnResponseDto> CreateAsync(AddOnWriteDto dto, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(dto, cancellationToken);
        var name = dto.AddOnName.Trim();
        if (await _addOns.NameExistsAsync(name, null, cancellationToken))
            throw new ConflictException("An add-on with this name already exists.");

        var addOn = new AddOn
        {
            AddOnName = name,
            Price = dto.Price,
            IsActive = true
        };
        await _addOns.AddAsync(addOn, cancellationToken);
        try
        {
            await _addOns.SaveAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DuplicateKey.Is(exception))
        {
            throw new ConflictException("An add-on with this name already exists.");
        }

        _logger.LogInformation("Created add-on {AddOnId}", addOn.AddOnId);
        return ToDto(addOn);
    }

    public async Task<AddOnResponseDto> UpdateAsync(int addOnId, AddOnWriteDto dto, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(dto, cancellationToken);
        var addOn = await _addOns.FindAsync(addOnId, cancellationToken)
            ?? throw new NotFoundException("Add-on", addOnId);
        var name = dto.AddOnName.Trim();
        if (await _addOns.NameExistsAsync(name, addOnId, cancellationToken))
            throw new ConflictException("An add-on with this name already exists.");

        addOn.AddOnName = name;
        addOn.Price = dto.Price;
        try
        {
            await _addOns.SaveAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DuplicateKey.Is(exception))
        {
            throw new ConflictException("An add-on with this name already exists.");
        }

        return ToDto(addOn);
    }

    public async Task<AddOnResponseDto> SetActiveAsync(int addOnId, bool isActive, CancellationToken cancellationToken)
    {
        var addOn = await _addOns.FindAsync(addOnId, cancellationToken)
            ?? throw new NotFoundException("Add-on", addOnId);
        addOn.IsActive = isActive;
        await _addOns.SaveAsync(cancellationToken);
        return ToDto(addOn);
    }

    private static AddOnResponseDto ToDto(AddOn addOn) => new()
    {
        AddOnId = addOn.AddOnId,
        AddOnName = addOn.AddOnName,
        Price = addOn.Price,
        IsActive = addOn.IsActive
    };
}
