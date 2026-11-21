namespace GreenCarWash.BookingService.Services;

public static class ImageSignatures
{
    public const int MaxBytes = 5 * 1024 * 1024;

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool TryGetExtension(string? contentType, ReadOnlySpan<byte> content, out string extension)
    {
        extension = string.Empty;
        var mediaType = contentType?.Split(';', 2)[0].Trim();
        if (mediaType is null || content.IsEmpty)
            return false;

        if (mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) && StartsWith(content, Jpeg))
        {
            extension = ".jpg";
            return true;
        }

        if (mediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase) && StartsWith(content, Png))
        {
            extension = ".png";
            return true;
        }

        return false;
    }

    private static bool StartsWith(ReadOnlySpan<byte> content, ReadOnlySpan<byte> signature) =>
        content.Length >= signature.Length && content[..signature.Length].SequenceEqual(signature);
}

public interface IBookingImageStore
{
    Task<string> SaveAsync(int bookingId, string extension, byte[] content, CancellationToken cancellationToken);
}

public sealed class DiskBookingImageStore : IBookingImageStore
{
    private readonly string _directory;

    public DiskBookingImageStore(IWebHostEnvironment environment)
    {
        _directory = Path.Combine(environment.ContentRootPath, "uploads", "bookings");
    }

    public async Task<string> SaveAsync(int bookingId, string extension, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var name = $"{bookingId}-{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(_directory, name), content, cancellationToken);
        return $"/uploads/bookings/{name}";
    }
}
