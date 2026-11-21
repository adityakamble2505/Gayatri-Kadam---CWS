-- GreenCarWash SQL Server schema. Idempotent: safe to run more than once.
-- Seven databases. No cross-database foreign keys.
-- Times are UTC DATETIME2(6). Money is DECIMAL(10,2). Enums are NVARCHAR plus a named CHECK.
-- UpdatedAt has no server-side ON UPDATE. The application sets it from IClock.
-- A unique index on a nullable column is filtered so more than one NULL is allowed.

IF DB_ID(N'AuthDB') IS NULL CREATE DATABASE AuthDB;
GO
IF DB_ID(N'CustomerDB') IS NULL CREATE DATABASE CustomerDB;
GO
IF DB_ID(N'WasherDB') IS NULL CREATE DATABASE WasherDB;
GO
IF DB_ID(N'CatalogDB') IS NULL CREATE DATABASE CatalogDB;
GO
IF DB_ID(N'BookingDB') IS NULL CREATE DATABASE BookingDB;
GO
IF DB_ID(N'PaymentDB') IS NULL CREATE DATABASE PaymentDB;
GO
IF DB_ID(N'NotificationDB') IS NULL CREATE DATABASE NotificationDB;
GO

-- ============ AuthDB ============
USE AuthDB;
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users (
        UserId           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        FullName         NVARCHAR(100) NOT NULL,
        Email            NVARCHAR(256) NOT NULL,
        PasswordHash     NVARCHAR(500) NOT NULL,
        PhoneNumber      NVARCHAR(20)  NOT NULL,
        Role             NVARCHAR(20)  NOT NULL,
        IsActive         BIT           NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
        FailedLoginCount INT           NOT NULL CONSTRAINT DF_Users_FailedLoginCount DEFAULT 0,
        LockoutEndUtc    DATETIME2(6)  NULL,
        LastLoginUtc     DATETIME2(6)  NULL,
        CreatedAt        DATETIME2(6)  NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt        DATETIME2(6)  NOT NULL CONSTRAINT DF_Users_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_Users_Role CHECK (Role IN (N'Customer', N'Washer', N'Admin'))
    );
    CREATE UNIQUE INDEX UX_Users_Email ON dbo.Users (Email);
    CREATE INDEX IX_Users_Role_Active ON dbo.Users (Role, IsActive);
END
GO

IF OBJECT_ID(N'dbo.Cities', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cities (
        CityId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Cities PRIMARY KEY,
        CityName NVARCHAR(100) NOT NULL,
        State    NVARCHAR(100) NOT NULL,
        IsActive BIT           NOT NULL CONSTRAINT DF_Cities_IsActive DEFAULT 1
    );
    CREATE UNIQUE INDEX UX_Cities_Name_State ON dbo.Cities (CityName, State);
END
GO

IF OBJECT_ID(N'dbo.RefreshTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RefreshTokens (
        TokenId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RefreshTokens PRIMARY KEY,
        UserId    INT          NOT NULL,
        TokenHash CHAR(64)     NOT NULL,
        ExpiresAt DATETIME2(6) NOT NULL,
        RevokedAt DATETIME2(6) NULL,
        CreatedAt DATETIME2(6) NOT NULL CONSTRAINT DF_RT_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_RT_User FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_RT_Hash ON dbo.RefreshTokens (TokenHash);
    CREATE INDEX IX_RT_User ON dbo.RefreshTokens (UserId, RevokedAt);
END
GO

-- ============ CustomerDB ============
USE CustomerDB;
GO

IF OBJECT_ID(N'dbo.CustomerProfiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerProfiles (
        CustomerId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerProfiles PRIMARY KEY,
        UserId     INT           NOT NULL,
        CityId     INT           NOT NULL,
        Address    NVARCHAR(500) NOT NULL,
        Latitude   DECIMAL(9,6)  NULL,
        Longitude  DECIMAL(9,6)  NULL,
        CreatedAt  DATETIME2(6)  NOT NULL CONSTRAINT DF_Cust_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt  DATETIME2(6)  NOT NULL CONSTRAINT DF_Cust_UpdatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_Cust_User ON dbo.CustomerProfiles (UserId);
    CREATE INDEX IX_Cust_City ON dbo.CustomerProfiles (CityId);
END
GO

IF OBJECT_ID(N'dbo.Vehicles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Vehicles (
        VehicleId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Vehicles PRIMARY KEY,
        CustomerId    INT           NOT NULL,
        VehicleNumber NVARCHAR(20)  NOT NULL,
        Brand         NVARCHAR(60)  NOT NULL,
        Model         NVARCHAR(60)  NOT NULL,
        VehicleType   NVARCHAR(20)  NOT NULL,
        ImageUrl      NVARCHAR(500) NULL,
        IsActive      BIT           NOT NULL CONSTRAINT DF_Veh_IsActive DEFAULT 1,
        CreatedAt     DATETIME2(6)  NOT NULL CONSTRAINT DF_Veh_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_Veh_Type CHECK (VehicleType IN (N'Sedan', N'SUV', N'Hatchback')),
        CONSTRAINT FK_Veh_Cust FOREIGN KEY (CustomerId) REFERENCES dbo.CustomerProfiles (CustomerId)
    );
    CREATE UNIQUE INDEX UX_Veh_Cust_Number ON dbo.Vehicles (CustomerId, VehicleNumber);
    CREATE INDEX IX_Veh_Cust_Active ON dbo.Vehicles (CustomerId, IsActive);
END
GO

-- ============ WasherDB ============
USE WasherDB;
GO

IF OBJECT_ID(N'dbo.WasherProfiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WasherProfiles (
        WasherId             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WasherProfiles PRIMARY KEY,
        UserId               INT           NOT NULL,
        CityId               INT           NOT NULL,
        IsAvailable          BIT           NOT NULL CONSTRAINT DF_Washer_IsAvailable DEFAULT 0,
        IsActive             BIT           NOT NULL CONSTRAINT DF_Washer_IsActive DEFAULT 1,
        IsQualified          BIT           NOT NULL CONSTRAINT DF_Washer_IsQualified DEFAULT 1,
        CurrentBookingId     INT           NULL,
        AssignedAt           DATETIME2(6)  NULL,
        AssignmentStatus     NVARCHAR(20)  NOT NULL CONSTRAINT DF_Washer_Assignment DEFAULT N'Available',
        Latitude             DECIMAL(9,6)  NULL,
        Longitude            DECIMAL(9,6)  NULL,
        LastLocationAt       DATETIME2(6)  NULL,
        AvgRating            DECIMAL(3,2)  NOT NULL CONSTRAINT DF_Washer_AvgRating DEFAULT 0.00,
        TotalReviews         INT           NOT NULL CONSTRAINT DF_Washer_TotalReviews DEFAULT 0,
        TotalCompletedWashes INT           NOT NULL CONSTRAINT DF_Washer_Completed DEFAULT 0,
        ProfilePictureUrl    NVARCHAR(500) NULL,
        CreatedAt            DATETIME2(6)  NOT NULL CONSTRAINT DF_Washer_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt            DATETIME2(6)  NOT NULL CONSTRAINT DF_Washer_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_Washer_Rating CHECK (AvgRating BETWEEN 0 AND 5),
        CONSTRAINT CK_Washer_Assignment CHECK (AssignmentStatus IN (N'Available', N'Assigned', N'Accepted', N'Rejected', N'InProgress'))
    );
    CREATE UNIQUE INDEX UX_Washer_User ON dbo.WasherProfiles (UserId);
    CREATE INDEX IX_Washer_Dispatch ON dbo.WasherProfiles (CityId, IsActive, IsAvailable, AssignmentStatus, AvgRating);
END
GO

-- ============ CatalogDB ============
USE CatalogDB;
GO

IF OBJECT_ID(N'dbo.Services', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Services (
        ServiceId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Services PRIMARY KEY,
        ServiceName      NVARCHAR(100) NOT NULL,
        Description      NVARCHAR(500) NULL,
        Price            DECIMAL(10,2) NOT NULL,
        Discount         INT           NOT NULL CONSTRAINT DF_Service_Discount DEFAULT 0,
        Duration         INT           NOT NULL,
        WaterSavedLiters INT           NOT NULL CONSTRAINT DF_Service_Water DEFAULT 0,
        IsActive         BIT           NOT NULL CONSTRAINT DF_Service_IsActive DEFAULT 1,
        CreatedAt        DATETIME2(6)  NOT NULL CONSTRAINT DF_Service_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt        DATETIME2(6)  NOT NULL CONSTRAINT DF_Service_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_Service_Price CHECK (Price >= 0),
        CONSTRAINT CK_Service_Discount CHECK (Discount BETWEEN 0 AND 100),
        CONSTRAINT CK_Service_Duration CHECK (Duration > 0)
    );
    CREATE UNIQUE INDEX UX_Service_Name ON dbo.Services (ServiceName);
END
GO

IF OBJECT_ID(N'dbo.AddOns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AddOns (
        AddOnId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AddOns PRIMARY KEY,
        AddOnName NVARCHAR(100) NOT NULL,
        Price     DECIMAL(10,2) NOT NULL,
        IsActive  BIT           NOT NULL CONSTRAINT DF_AddOn_IsActive DEFAULT 1,
        CONSTRAINT CK_AddOn_Price CHECK (Price >= 0)
    );
    CREATE UNIQUE INDEX UX_AddOn_Name ON dbo.AddOns (AddOnName);
END
GO

IF OBJECT_ID(N'dbo.PromoCodes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PromoCodes (
        PromoId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PromoCodes PRIMARY KEY,
        Code           NVARCHAR(30)  NOT NULL,
        DiscountType   NVARCHAR(20)  NOT NULL,
        DiscountValue  DECIMAL(10,2) NOT NULL,
        MinOrderAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_Promo_Min DEFAULT 0,
        MaxDiscount    DECIMAL(10,2) NULL,
        ValidFrom      DATETIME2(6)  NOT NULL,
        ValidTo        DATETIME2(6)  NOT NULL,
        UsageLimit     INT           NULL,
        UsedCount      INT           NOT NULL CONSTRAINT DF_Promo_Used DEFAULT 0,
        IsActive       BIT           NOT NULL CONSTRAINT DF_Promo_IsActive DEFAULT 1,
        CONSTRAINT CK_Promo_Type CHECK (DiscountType IN (N'Percent', N'Flat')),
        CONSTRAINT CK_Promo_Value CHECK (DiscountValue > 0),
        CONSTRAINT CK_Promo_Dates CHECK (ValidTo > ValidFrom)
    );
    CREATE UNIQUE INDEX UX_Promo_Code ON dbo.PromoCodes (Code);
END
GO

-- ============ BookingDB ============
USE BookingDB;
GO

IF OBJECT_ID(N'dbo.Bookings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Bookings (
        BookingId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Bookings PRIMARY KEY,
        BookingNumber        NVARCHAR(20)  NOT NULL,
        CustomerId           INT           NOT NULL,
        VehicleId            INT           NOT NULL,
        ServiceId            INT           NOT NULL,
        WasherId             INT           NULL,
        CityId               INT           NOT NULL,
        BookingType          NVARCHAR(20)  NOT NULL,
        BookingDate          DATETIME2(6)  NOT NULL,
        EstimatedDurationMin INT           NOT NULL,
        Address              NVARCHAR(500) NOT NULL,
        Latitude             DECIMAL(9,6)  NULL,
        Longitude            DECIMAL(9,6)  NULL,
        Instructions         NVARCHAR(1000) NULL,
        ServiceNameSnapshot  NVARCHAR(100) NOT NULL,
        ServicePriceSnapshot DECIMAL(10,2) NOT NULL,
        ServiceDiscountPct   INT           NOT NULL CONSTRAINT DF_Booking_Discount DEFAULT 0,
        AddOnsTotal          DECIMAL(10,2) NOT NULL CONSTRAINT DF_Booking_AddOns DEFAULT 0,
        PromoCode            NVARCHAR(30)  NULL,
        PromoDiscount        DECIMAL(10,2) NOT NULL CONSTRAINT DF_Booking_Promo DEFAULT 0,
        TotalAmount          DECIMAL(10,2) NOT NULL,
        WaterSavedLiters     INT           NOT NULL CONSTRAINT DF_Booking_Water DEFAULT 0,
        Status               NVARCHAR(20)  NOT NULL,
        DispatchStartedAt    DATETIME2(6)  NULL,
        NeedsAdminAttention  BIT           NOT NULL CONSTRAINT DF_Booking_Admin DEFAULT 0,
        AfterWashPhotoUrl    NVARCHAR(500) NULL,
        AcceptedAt           DATETIME2(6)  NULL,
        StartedAt            DATETIME2(6)  NULL,
        CompletedAt          DATETIME2(6)  NULL,
        CancelledAt          DATETIME2(6)  NULL,
        CancelledBy          NVARCHAR(20)  NULL,
        CancelReason         NVARCHAR(500) NULL,
        RowVersion           INT           NOT NULL CONSTRAINT DF_Booking_RowVersion DEFAULT 0,
        CreatedAt            DATETIME2(6)  NOT NULL CONSTRAINT DF_Booking_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt            DATETIME2(6)  NOT NULL CONSTRAINT DF_Booking_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_Booking_Type CHECK (BookingType IN (N'Immediate', N'Scheduled')),
        CONSTRAINT CK_Booking_Status CHECK (Status IN (N'Pending', N'Scheduled', N'Accepted', N'InProgress', N'Completed', N'Cancelled')),
        CONSTRAINT CK_Booking_CancelledBy CHECK (CancelledBy IS NULL OR CancelledBy IN (N'Customer', N'Washer', N'Admin', N'System')),
        CONSTRAINT CK_Booking_Total CHECK (TotalAmount >= 0),
        CONSTRAINT CK_Booking_Duration CHECK (EstimatedDurationMin > 0)
    );
    CREATE UNIQUE INDEX UX_Booking_Number ON dbo.Bookings (BookingNumber);
    CREATE INDEX IX_Booking_Customer ON dbo.Bookings (CustomerId, CreatedAt);
    CREATE INDEX IX_Booking_Washer ON dbo.Bookings (WasherId, Status, BookingDate);
    CREATE INDEX IX_Booking_Dispatch ON dbo.Bookings (Status, WasherId, DispatchStartedAt, BookingDate);
    CREATE INDEX IX_Booking_City_Status ON dbo.Bookings (CityId, Status, BookingDate);
    CREATE INDEX IX_Booking_Completed ON dbo.Bookings (Status, CompletedAt);
    CREATE INDEX IX_Booking_Service ON dbo.Bookings (ServiceId, Status);
    CREATE INDEX IX_Booking_Customer_Status_Date ON dbo.Bookings (CustomerId, Status, BookingDate);
    CREATE INDEX IX_Booking_Reminder ON dbo.Bookings (Status, BookingType, BookingDate);
    CREATE INDEX IX_Booking_Leaderboard ON dbo.Bookings (Status, CustomerId, WaterSavedLiters);
    CREATE INDEX IX_Booking_Status_Date ON dbo.Bookings (Status, BookingDate);
    CREATE INDEX IX_Booking_Completed_City ON dbo.Bookings (Status, CityId, CompletedAt);
    CREATE INDEX IX_Booking_Washer_Performance ON dbo.Bookings (Status, WasherId, StartedAt, CompletedAt);
END
GO

IF OBJECT_ID(N'dbo.BookingAddOns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingAddOns (
        BookingAddOnId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BookingAddOns PRIMARY KEY,
        BookingId      INT           NOT NULL,
        AddOnId        INT           NOT NULL,
        NameSnapshot   NVARCHAR(100) NOT NULL,
        PriceSnapshot  DECIMAL(10,2) NOT NULL,
        CONSTRAINT FK_BA_Booking FOREIGN KEY (BookingId) REFERENCES dbo.Bookings (BookingId) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_BA ON dbo.BookingAddOns (BookingId, AddOnId);
    CREATE INDEX IX_BA_AddOn ON dbo.BookingAddOns (AddOnId);
END
GO

IF OBJECT_ID(N'dbo.BookingStatusHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingStatusHistory (
        HistoryId       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BookingStatusHistory PRIMARY KEY,
        BookingId       INT           NOT NULL,
        FromStatus      NVARCHAR(20)  NULL,
        ToStatus        NVARCHAR(20)  NOT NULL,
        ChangedByUserId INT           NULL,
        ChangedByRole   NVARCHAR(20)  NOT NULL,
        Note            NVARCHAR(500) NULL,
        ChangedAt       DATETIME2(6)  NOT NULL CONSTRAINT DF_BSH_ChangedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_BSH_Booking FOREIGN KEY (BookingId) REFERENCES dbo.Bookings (BookingId) ON DELETE CASCADE
    );
    CREATE INDEX IX_BSH_Booking ON dbo.BookingStatusHistory (BookingId, ChangedAt);
END
GO

IF OBJECT_ID(N'dbo.BookingOffers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingOffers (
        OfferId     BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BookingOffers PRIMARY KEY,
        BookingId   INT          NOT NULL,
        WasherId    INT          NOT NULL,
        OfferedAt   DATETIME2(6) NOT NULL,
        ExpiresAt   DATETIME2(6) NOT NULL,
        Response    NVARCHAR(20) NOT NULL CONSTRAINT DF_Offer_Response DEFAULT N'Pending',
        RespondedAt DATETIME2(6) NULL,
        CONSTRAINT CK_Offer_Response CHECK (Response IN (N'Pending', N'Accepted', N'Declined', N'Expired')),
        CONSTRAINT FK_Offer_Booking FOREIGN KEY (BookingId) REFERENCES dbo.Bookings (BookingId) ON DELETE CASCADE
    );
    CREATE INDEX IX_Offer_Booking ON dbo.BookingOffers (BookingId, Response);
    CREATE INDEX IX_Offer_Pending ON dbo.BookingOffers (Response, ExpiresAt);
    CREATE INDEX IX_Offer_Washer ON dbo.BookingOffers (WasherId, OfferedAt);
END
GO

IF OBJECT_ID(N'dbo.Reviews', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Reviews (
        ReviewId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Reviews PRIMARY KEY,
        BookingId    INT            NOT NULL,
        ReviewerRole NVARCHAR(20)   NOT NULL,
        CustomerId   INT            NOT NULL,
        WasherId     INT            NOT NULL,
        Rating       TINYINT        NOT NULL,
        Comments     NVARCHAR(1000) NULL,
        CreatedAt    DATETIME2(6)   NOT NULL CONSTRAINT DF_Review_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_Review_Role CHECK (ReviewerRole IN (N'Customer', N'Washer')),
        CONSTRAINT CK_Review_Rating CHECK (Rating BETWEEN 1 AND 5),
        CONSTRAINT FK_Review_Booking FOREIGN KEY (BookingId) REFERENCES dbo.Bookings (BookingId)
    );
    CREATE UNIQUE INDEX UX_Review_Booking_Role ON dbo.Reviews (BookingId, ReviewerRole);
    CREATE INDEX IX_Review_Washer ON dbo.Reviews (WasherId, ReviewerRole, CreatedAt);
END
GO

IF OBJECT_ID(N'dbo.OutboxEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OutboxEvents (
        EventId       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BookingOutbox PRIMARY KEY,
        EventType     NVARCHAR(80)   NOT NULL,
        Payload       NVARCHAR(MAX)  NOT NULL,
        CreatedAt     DATETIME2(6)   NOT NULL CONSTRAINT DF_BookingOutbox_CreatedAt DEFAULT SYSUTCDATETIME(),
        ProcessedAt   DATETIME2(6)   NULL,
        RetryCount    INT            NOT NULL CONSTRAINT DF_BookingOutbox_Retry DEFAULT 0,
        NextAttemptAt DATETIME2(6)   NOT NULL CONSTRAINT DF_BookingOutbox_Next DEFAULT SYSUTCDATETIME(),
        LastError     NVARCHAR(500)  NULL,
        CONSTRAINT CK_BookingOutbox_Payload CHECK (ISJSON(Payload) = 1)
    );
    CREATE INDEX IX_Outbox_Pending ON dbo.OutboxEvents (ProcessedAt, NextAttemptAt, EventId);
END
GO

-- ============ PaymentDB ============
USE PaymentDB;
GO

IF OBJECT_ID(N'dbo.Payments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Payments (
        PaymentId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Payments PRIMARY KEY,
        BookingId            INT           NOT NULL,
        CustomerId           INT           NOT NULL,
        Amount               DECIMAL(10,2) NOT NULL,
        Currency             CHAR(3)       NOT NULL CONSTRAINT DF_Pay_Currency DEFAULT 'USD',
        PaymentMethod        NVARCHAR(20)  NOT NULL,
        CardBrand            NVARCHAR(20)  NULL,
        CardLast4            CHAR(4)       NULL,
        PaymentStatus        NVARCHAR(20)  NOT NULL CONSTRAINT DF_Pay_Status DEFAULT N'Pending',
        TransactionReference NVARCHAR(100) NULL,
        IdempotencyKey       NVARCHAR(64)  NOT NULL,
        FailureReason        NVARCHAR(300) NULL,
        RefundedAmount       DECIMAL(10,2) NOT NULL CONSTRAINT DF_Pay_Refunded DEFAULT 0,
        PaymentDate          DATETIME2(6)  NULL,
        CreatedAt            DATETIME2(6)  NOT NULL CONSTRAINT DF_Pay_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt            DATETIME2(6)  NOT NULL CONSTRAINT DF_Pay_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_Pay_Method CHECK (PaymentMethod IN (N'Card', N'Simulated')),
        CONSTRAINT CK_Pay_Status CHECK (PaymentStatus IN (N'Pending', N'Success', N'Failed', N'Refunded', N'PartiallyRefunded')),
        CONSTRAINT CK_Pay_Amount CHECK (Amount > 0),
        CONSTRAINT CK_Pay_Refund CHECK (RefundedAmount >= 0 AND RefundedAmount <= Amount)
    );
    CREATE UNIQUE INDEX UX_Pay_Booking ON dbo.Payments (BookingId);
    CREATE UNIQUE INDEX UX_Pay_Idem ON dbo.Payments (IdempotencyKey);
    -- A normal unique index allows only one NULL. Pending rows have no reference yet.
    CREATE UNIQUE INDEX UX_Pay_Tx ON dbo.Payments (TransactionReference) WHERE TransactionReference IS NOT NULL;
    CREATE INDEX IX_Pay_Customer ON dbo.Payments (CustomerId, CreatedAt);
    CREATE INDEX IX_Pay_Status ON dbo.Payments (PaymentStatus, PaymentDate);
END
GO

IF OBJECT_ID(N'dbo.PaymentRefunds', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentRefunds (
        RefundId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentRefunds PRIMARY KEY,
        PaymentId         INT           NOT NULL,
        Amount            DECIMAL(10,2) NOT NULL,
        Reason            NVARCHAR(300) NOT NULL,
        ProcessedByUserId INT           NOT NULL,
        CreatedAt         DATETIME2(6)  NOT NULL CONSTRAINT DF_Refund_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Refund_Payment FOREIGN KEY (PaymentId) REFERENCES dbo.Payments (PaymentId)
    );
    CREATE INDEX IX_Refund_Payment ON dbo.PaymentRefunds (PaymentId);
END
GO

IF OBJECT_ID(N'dbo.OutboxEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OutboxEvents (
        EventId       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentOutbox PRIMARY KEY,
        EventType     NVARCHAR(80)  NOT NULL,
        Payload       NVARCHAR(MAX) NOT NULL,
        CreatedAt     DATETIME2(6)  NOT NULL CONSTRAINT DF_PaymentOutbox_CreatedAt DEFAULT SYSUTCDATETIME(),
        ProcessedAt   DATETIME2(6)  NULL,
        RetryCount    INT           NOT NULL CONSTRAINT DF_PaymentOutbox_Retry DEFAULT 0,
        NextAttemptAt DATETIME2(6)  NOT NULL CONSTRAINT DF_PaymentOutbox_Next DEFAULT SYSUTCDATETIME(),
        LastError     NVARCHAR(500) NULL,
        CONSTRAINT CK_PaymentOutbox_Payload CHECK (ISJSON(Payload) = 1)
    );
    CREATE INDEX IX_Outbox_Pending ON dbo.OutboxEvents (ProcessedAt, NextAttemptAt, EventId);
END
GO

-- ============ NotificationDB ============
USE NotificationDB;
GO

IF OBJECT_ID(N'dbo.Notifications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Notifications (
        NotificationId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY,
        UserId         INT            NOT NULL,
        Type           NVARCHAR(40)   NOT NULL,
        Title          NVARCHAR(150)  NOT NULL,
        Message        NVARCHAR(1000) NOT NULL,
        ReferenceType  NVARCHAR(30)   NULL,
        ReferenceId    INT            NULL,
        IsRead         BIT            NOT NULL CONSTRAINT DF_Notif_IsRead DEFAULT 0,
        ReadAt         DATETIME2(6)   NULL,
        CreatedAt      DATETIME2(6)   NOT NULL CONSTRAINT DF_Notif_CreatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_Notif_User ON dbo.Notifications (UserId, IsRead, CreatedAt);
    CREATE INDEX IX_Notif_Ref ON dbo.Notifications (ReferenceType, ReferenceId);
    -- Filtered so several rows with a NULL reference can coexist. SQL Server allows one NULL in an unfiltered unique index.
    CREATE UNIQUE INDEX UX_Notif_Idempotency ON dbo.Notifications (Type, ReferenceType, ReferenceId, UserId)
        WHERE ReferenceType IS NOT NULL AND ReferenceId IS NOT NULL;
END
GO

IF OBJECT_ID(N'dbo.EmailLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmailLogs (
        EmailLogId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmailLogs PRIMARY KEY,
        UserId     INT           NULL,
        ToEmail    NVARCHAR(256) NOT NULL,
        Subject    NVARCHAR(200) NOT NULL,
        Status     NVARCHAR(20)  NOT NULL CONSTRAINT DF_Email_Status DEFAULT N'Queued',
        Attempts   INT           NOT NULL CONSTRAINT DF_Email_Attempts DEFAULT 0,
        LastError  NVARCHAR(500) NULL,
        CreatedAt  DATETIME2(6)  NOT NULL CONSTRAINT DF_Email_CreatedAt DEFAULT SYSUTCDATETIME(),
        SentAt     DATETIME2(6)  NULL,
        CONSTRAINT CK_Email_Status CHECK (Status IN (N'Queued', N'Sent', N'Failed'))
    );
    CREATE INDEX IX_Email_Status ON dbo.EmailLogs (Status, CreatedAt);
END
GO
