-- Sample data for an empty GreenCarWash database (SQL Server).
-- Run Docs/schema.sql first. Inserts are skipped when the natural key already exists.
-- Times are UTC. Money is decimal(10,2). Enum values are the schema strings.
-- The admin password hash is a placeholder, not a login secret. Replace it
-- with a real BCrypt hash before any authentication test.

USE AuthDB;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Email = N'admin@greencarwash.example')
INSERT INTO dbo.Users (FullName, Email, PasswordHash, PhoneNumber, Role, IsActive)
VALUES (
    N'GreenCarWash Admin',
    N'admin@greencarwash.example',
    N'$2a$11$PLACEHOLDER_BCRYPT_HASH_REPLACE_BEFORE_ANY_LOGIN_000000',
    N'+12125550100',
    N'Admin',
    1
);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Cities WHERE CityName = N'New York' AND State = N'NY')
    INSERT INTO dbo.Cities (CityName, State, IsActive) VALUES (N'New York', N'NY', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Cities WHERE CityName = N'Los Angeles' AND State = N'CA')
    INSERT INTO dbo.Cities (CityName, State, IsActive) VALUES (N'Los Angeles', N'CA', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Cities WHERE CityName = N'Chicago' AND State = N'IL')
    INSERT INTO dbo.Cities (CityName, State, IsActive) VALUES (N'Chicago', N'IL', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Cities WHERE CityName = N'Houston' AND State = N'TX')
    INSERT INTO dbo.Cities (CityName, State, IsActive) VALUES (N'Houston', N'TX', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Cities WHERE CityName = N'Phoenix' AND State = N'AZ')
    INSERT INTO dbo.Cities (CityName, State, IsActive) VALUES (N'Phoenix', N'AZ', 1);
GO

USE CatalogDB;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Services WHERE ServiceName = N'Basic')
    INSERT INTO dbo.Services (ServiceName, Description, Price, Discount, Duration, WaterSavedLiters, IsActive)
    VALUES (N'Basic', N'Exterior hand wash and dry', 19.99, 0, 30, 15, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Services WHERE ServiceName = N'Premium')
    INSERT INTO dbo.Services (ServiceName, Description, Price, Discount, Duration, WaterSavedLiters, IsActive)
    VALUES (N'Premium', N'Wash, wax, and interior wipe-down', 39.99, 0, 45, 30, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Services WHERE ServiceName = N'Deluxe')
    INSERT INTO dbo.Services (ServiceName, Description, Price, Discount, Duration, WaterSavedLiters, IsActive)
    VALUES (N'Deluxe', N'Full detail with a water-saving cycle', 69.99, 0, 75, 50, 1);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.AddOns WHERE AddOnName = N'Interior Vacuum')
    INSERT INTO dbo.AddOns (AddOnName, Price, IsActive) VALUES (N'Interior Vacuum', 14.99, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.AddOns WHERE AddOnName = N'Ceramic Wax')
    INSERT INTO dbo.AddOns (AddOnName, Price, IsActive) VALUES (N'Ceramic Wax', 24.99, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.AddOns WHERE AddOnName = N'Tire Dressing')
    INSERT INTO dbo.AddOns (AddOnName, Price, IsActive) VALUES (N'Tire Dressing', 9.99, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.AddOns WHERE AddOnName = N'Engine Wash')
    INSERT INTO dbo.AddOns (AddOnName, Price, IsActive) VALUES (N'Engine Wash', 19.99, 1);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PromoCodes WHERE Code = N'WELCOME10')
    INSERT INTO dbo.PromoCodes
        (Code, DiscountType, DiscountValue, MinOrderAmount, MaxDiscount, ValidFrom, ValidTo, UsageLimit, UsedCount, IsActive)
    VALUES
        (N'WELCOME10', N'Percent', 10.00, 0.00, 15.00,
         '2026-01-01T00:00:00.000000', '2027-01-01T00:00:00.000000', NULL, 0, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.PromoCodes WHERE Code = N'FLAT5')
    INSERT INTO dbo.PromoCodes
        (Code, DiscountType, DiscountValue, MinOrderAmount, MaxDiscount, ValidFrom, ValidTo, UsageLimit, UsedCount, IsActive)
    VALUES
        (N'FLAT5', N'Flat', 5.00, 25.00, NULL,
         '2026-01-01T00:00:00.000000', '2027-01-01T00:00:00.000000', 1000, 0, 1);
GO
