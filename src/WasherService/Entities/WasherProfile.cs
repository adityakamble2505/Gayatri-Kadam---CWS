namespace GreenCarWash.WasherService.Entities;

public sealed class WasherProfile
{
    public int WasherId { get; set; }

    public int UserId { get; set; }

    public int CityId { get; set; }

    public bool IsAvailable { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsQualified { get; set; } = true;

    public int? CurrentBookingId { get; set; }

    public DateTime? AssignedAt { get; set; }

    public AssignmentStatus AssignmentStatus { get; set; } = AssignmentStatus.Available;

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public DateTime? LastLocationAt { get; set; }

    public decimal AvgRating { get; set; }

    public int TotalReviews { get; set; }

    public int TotalCompletedWashes { get; set; }

    public string? ProfilePictureUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
