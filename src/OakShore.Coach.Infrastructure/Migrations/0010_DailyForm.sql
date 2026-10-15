-- GarminResponse archives the training status payload with every array-valued member removed
-- (ADR 0010, ADR 0012). Only code promoting a field into a column reads it; no MCP tool and no
-- query-time path does.
IF OBJECT_ID(N'dbo.TrainingLoads', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TrainingLoads
    (
        UserId NVARCHAR(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CalendarDate DATE NOT NULL,
        WeeklyTrainingLoad INT NULL,
        LoadTunnelMin INT NULL,
        LoadTunnelMax INT NULL,
        GarminResponse NVARCHAR(MAX) NULL,
        CONSTRAINT PK_TrainingLoads PRIMARY KEY (UserId, CalendarDate)
    );
END;

-- Keyed by the date Garmin computed the estimate, so re-reading an unchanged one adds nothing.
IF OBJECT_ID(N'dbo.VO2maxEstimates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.VO2maxEstimates
    (
        UserId NVARCHAR(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Sport NVARCHAR(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CalendarDate DATE NOT NULL,
        MlPerKgMin FLOAT NOT NULL,
        CONSTRAINT PK_VO2maxEstimates PRIMARY KEY (UserId, Sport, CalendarDate)
    );
END;
