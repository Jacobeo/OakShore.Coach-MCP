IF OBJECT_ID(N'dbo.SportProfiles', N'U') IS NULL
BEGIN
    -- One row per observed set of boundaries; a change adds a Revision rather than
    -- overwriting, so an Activity pinned to an earlier one keeps it.
    CREATE TABLE dbo.SportProfiles
    (
        UserId NVARCHAR(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Name NVARCHAR(50) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Revision INT NOT NULL,
        TrainingMethod NVARCHAR(50) NULL,
        RestingHeartRateBpm INT NULL,
        MaxHeartRateBpm INT NULL,
        LactateThresholdHeartRateBpm INT NULL,
        FirstObservedAt DATETIMEOFFSET(7) NOT NULL,
        LastObservedAt DATETIMEOFFSET(7) NOT NULL,
        CONSTRAINT PK_SportProfiles PRIMARY KEY (UserId, Name, Revision)
    );
END;

IF OBJECT_ID(N'dbo.HeartRateZones', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HeartRateZones
    (
        UserId NVARCHAR(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SportProfileName NVARCHAR(50) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SportProfileRevision INT NOT NULL,
        ZoneNumber INT NOT NULL,
        LowBoundaryBpm INT NOT NULL,
        CONSTRAINT PK_HeartRateZones PRIMARY KEY (UserId, SportProfileName, SportProfileRevision, ZoneNumber),
        CONSTRAINT FK_HeartRateZones_SportProfiles FOREIGN KEY (UserId, SportProfileName, SportProfileRevision)
            REFERENCES dbo.SportProfiles (UserId, Name, Revision)
    );
END;

IF COL_LENGTH(N'dbo.Activities', N'SportProfileName') IS NULL
BEGIN
    ALTER TABLE dbo.Activities ADD
        SportProfileName NVARCHAR(50) COLLATE Latin1_General_100_BIN2 NULL,
        SportProfileRevision INT NULL,
        CONSTRAINT FK_Activities_SportProfiles FOREIGN KEY (UserId, SportProfileName, SportProfileRevision)
            REFERENCES dbo.SportProfiles (UserId, Name, Revision);
END;

IF OBJECT_ID(N'dbo.TimeInHeartRateZones', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TimeInHeartRateZones
    (
        UserId NVARCHAR(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ActivityId BIGINT NOT NULL,
        ZoneNumber INT NOT NULL,
        Seconds FLOAT NOT NULL,
        CONSTRAINT PK_TimeInHeartRateZones PRIMARY KEY (UserId, ActivityId, ZoneNumber),
        CONSTRAINT FK_TimeInHeartRateZones_Activities FOREIGN KEY (UserId, ActivityId)
            REFERENCES dbo.Activities (UserId, ActivityId)
    );
END;
