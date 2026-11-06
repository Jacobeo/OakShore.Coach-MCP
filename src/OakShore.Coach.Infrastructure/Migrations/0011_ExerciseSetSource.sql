IF COL_LENGTH(N'dbo.ExerciseSets', N'Source') IS NULL
BEGIN
    -- Every row stored before this column existed was read from Garmin's API.
    ALTER TABLE dbo.ExerciseSets ADD Source NVARCHAR(20) COLLATE Latin1_General_100_BIN2 NOT NULL
        CONSTRAINT DF_ExerciseSets_Source DEFAULT N'GARMIN_API';
END;
