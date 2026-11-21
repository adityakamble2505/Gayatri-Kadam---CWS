namespace GreenCarWash.WasherService.DTOs;

public sealed class WasherResponseDto
{
    public int WasherId { get; init; }

    public int UserId { get; init; }

    public int CityId { get; init; }

    public bool IsAvailable { get; init; }

    public bool IsActive { get; init; }

    public bool IsQualified { get; init; }

    public int? CurrentBookingId { get; init; }

    public DateTime? AssignedAt { get; init; }

    public string AssignmentStatus { get; init; } = string.Empty;

    public decimal? Latitude { get; init; }

    public decimal? Longitude { get; init; }

    public DateTime? LastLocationAt { get; init; }

    public decimal AvgRating { get; init; }

    public int TotalReviews { get; init; }

    public int TotalCompletedWashes { get; init; }

    public string? ProfilePictureUrl { get; init; }
}

public sealed class CreateWasherProfileDto
{
    public int CityId { get; set; }
}

public sealed class AvailabilityDto
{
    public bool IsAvailable { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }
}

public sealed class LocationDto
{
    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }
}

public sealed class AdminWasherWriteDto
{
    public int UserId { get; set; }

    public int CityId { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsQualified { get; set; } = true;
}

public sealed class AdminWasherUpdateDto
{
    public int CityId { get; set; }

    public bool IsAvailable { get; set; }

    public bool IsActive { get; set; }

    public bool IsQualified { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }
}

public sealed class QualifyDto
{
    public bool IsQualified { get; set; }
}

public sealed class PagedWashersDto
{
    public int Page { get; init; }

    public int PageSize { get; init; }

    public int Total { get; init; }

    public IReadOnlyList<WasherResponseDto> Items { get; init; } = [];
}

public sealed class NearbyWasherDto
{
    public int WasherId { get; init; }

    public int UserId { get; init; }

    public int CityId { get; init; }

    public decimal AvgRating { get; init; }

    public decimal Latitude { get; init; }

    public decimal Longitude { get; init; }

    public double Miles { get; init; }
}

public sealed class QueueEntryDto
{
    public int WasherId { get; init; }

    public int CityId { get; init; }

    public decimal AvgRating { get; init; }

    public decimal? Latitude { get; init; }

    public decimal? Longitude { get; init; }

    public DateTime EnqueuedAtUtc { get; init; }
}

public sealed class NextWasherDto
{
    public int CityId { get; set; }

    public List<int> ExcludeIds { get; set; } = [];

    public string? Strategy { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }
}

public sealed class NextWasherResultDto
{
    public int WasherId { get; init; }

    public int UserId { get; init; }
}

public sealed class ReleaseWasherDto
{
    public int WasherId { get; set; }

    public bool ToTail { get; set; }
}

public sealed class ConfirmAssignmentDto
{
    public int WasherId { get; set; }

    public int BookingId { get; set; }
}

public sealed class WasherIdDto
{
    public int WasherId { get; set; }
}

public sealed class AssignmentUpdateDto
{
    public int WasherId { get; set; }

    public string AssignmentStatus { get; set; } = string.Empty;

    public int? CurrentBookingId { get; set; }

    public bool ClearBooking { get; set; }
}

public sealed class RatingUpdateDto
{
    public int WasherId { get; set; }

    public int Rating { get; set; }
}
