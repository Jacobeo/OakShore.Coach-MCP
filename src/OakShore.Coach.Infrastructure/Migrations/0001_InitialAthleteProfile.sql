IF OBJECT_ID(N'dbo.AthleteProfiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AthleteProfiles
    (
        UserId NVARCHAR(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        BodyWeightKg DECIMAL(18, 1) NOT NULL,
        LastSyncedAt DATETIMEOFFSET(7) NOT NULL,
        CONSTRAINT PK_AthleteProfiles PRIMARY KEY (UserId)
    );
END;
