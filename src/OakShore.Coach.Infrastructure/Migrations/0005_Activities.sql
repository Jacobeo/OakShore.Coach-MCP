IF OBJECT_ID(N'dbo.Activities', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Activities
    (
        UserId NVARCHAR(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ActivityId BIGINT NOT NULL,
        StartTimeUtc DATETIMEOFFSET(7) NOT NULL,
        TypeKey NVARCHAR(100) NOT NULL,
        DurationSeconds FLOAT NOT NULL,
        DistanceMeters FLOAT NULL,
        TotalSets INT NULL,
        ActiveSets INT NULL,
        TotalReps INT NULL,
        CONSTRAINT PK_Activities PRIMARY KEY (UserId, ActivityId)
    );
END;

IF OBJECT_ID(N'dbo.IngestBatches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IngestBatches
    (
        UserId NVARCHAR(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        BatchHash VARCHAR(64) NOT NULL,
        FromDate DATE NOT NULL,
        ToDate DATE NOT NULL,
        LastSyncedAt DATETIMEOFFSET(7) NOT NULL,
        CONSTRAINT PK_IngestBatches PRIMARY KEY (UserId, BatchHash)
    );
    CREATE INDEX IX_IngestBatches_Freshness ON dbo.IngestBatches (UserId, LastSyncedAt DESC);
END;
