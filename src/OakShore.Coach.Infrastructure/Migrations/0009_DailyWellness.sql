-- GarminResponse archives Garmin's response with every array-valued member removed (ADR 0010).
-- Only code promoting a field into a column reads it; no MCP tool and no query-time path does.
IF OBJECT_ID(N'dbo.SleepNights', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SleepNights
    (
        UserId NVARCHAR(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CalendarDate DATE NOT NULL,
        StartTimeUtc DATETIMEOFFSET(7) NOT NULL,
        EndTimeUtc DATETIMEOFFSET(7) NOT NULL,
        SleepMinutes INT NULL,
        DeepSleepMinutes INT NULL,
        LightSleepMinutes INT NULL,
        RemSleepMinutes INT NULL,
        AwakeSleepMinutes INT NULL,
        UnmeasurableSleepMinutes INT NULL,
        GarminResponse NVARCHAR(MAX) NULL,
        CONSTRAINT PK_SleepNights PRIMARY KEY (UserId, CalendarDate)
    );
END;

IF OBJECT_ID(N'dbo.DailySummaries', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DailySummaries
    (
        UserId NVARCHAR(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CalendarDate DATE NOT NULL,
        BodyBatteryCharged INT NULL,
        BodyBatteryDrained INT NULL,
        BodyBatteryHighest INT NULL,
        BodyBatteryLowest INT NULL,
        StressLevelAverage INT NULL,
        StressLevelMaximum INT NULL,
        StressLevelRestSeconds INT NULL,
        StressLevelLowSeconds INT NULL,
        StressLevelMediumSeconds INT NULL,
        StressLevelHighSeconds INT NULL,
        GarminResponse NVARCHAR(MAX) NULL,
        CONSTRAINT PK_DailySummaries PRIMARY KEY (UserId, CalendarDate)
    );
END;

-- Version 3 batches state that every date in their range was synced for wellness.
IF COL_LENGTH(N'dbo.IngestBatches', N'Version') IS NULL
BEGIN
    ALTER TABLE dbo.IngestBatches ADD Version INT NOT NULL
        CONSTRAINT DF_IngestBatches_Version DEFAULT 2;
END;
