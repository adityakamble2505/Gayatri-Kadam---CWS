-- WasherDB queries (SQL Server).
-- geography::Point takes latitude first, then longitude. SRID 4326.
-- STDistance returns meters. Dividing by 1609.344 converts to miles.
-- The in-memory washer queue still uses haversine. This statement is the SQL report.
--
-- Q9: every SET expression reads the pre-update row. AvgRating and TotalReviews
-- both see the old TotalReviews, so the average is
-- ROUND((oldAvg * oldCount + @rating) / (oldCount + 1), 2).
-- 4.50 over 2 reviews plus a rating of 5 becomes 4.67 over 3.

USE WasherDB;
GO

DECLARE @cityId int = 1;
DECLARE @lat float = 40.712800;
DECLARE @lng float = -74.006000;
DECLARE @skip int = 0;
DECLARE @take int = 10;

SELECT WasherId,
       CityId,
       AvgRating,
       Latitude,
       Longitude,
       geography::Point(Latitude, Longitude, 4326).STDistance(geography::Point(@lat, @lng, 4326)) / 1609.344 AS Miles
FROM dbo.WasherProfiles
WHERE CityId = @cityId
  AND IsActive = 1
  AND IsAvailable = 1
  AND IsQualified = 1
  AND AssignmentStatus = N'Available'
  AND Latitude IS NOT NULL
  AND Longitude IS NOT NULL
ORDER BY Miles, AvgRating DESC
OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
GO

DECLARE @w int = (SELECT WasherId FROM dbo.WasherProfiles WHERE UserId = 101);
DECLARE @rating int = 5;

UPDATE dbo.WasherProfiles
SET AvgRating = ROUND((AvgRating * TotalReviews + @rating) / (TotalReviews + 1), 2),
    TotalReviews = TotalReviews + 1,
    UpdatedAt = SYSUTCDATETIME()
WHERE WasherId = @w
  AND TotalReviews = 2
  AND AvgRating = 4.50;

SELECT WasherId, AvgRating, TotalReviews
FROM dbo.WasherProfiles
WHERE UserId = 101;
GO

SELECT WasherId, CityId, AvgRating, Latitude, Longitude
FROM dbo.WasherProfiles
WHERE IsActive = 1
  AND IsAvailable = 1
  AND IsQualified = 1
  AND AssignmentStatus = N'Available';
GO
