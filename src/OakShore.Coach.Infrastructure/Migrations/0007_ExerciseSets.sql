IF OBJECT_ID(N'dbo.ExerciseSets', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExerciseSets
    (
        UserId NVARCHAR(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ActivityId BIGINT NOT NULL,
        Position INT NOT NULL,
        SetType NVARCHAR(50) NOT NULL,
        Repetitions INT NULL,
        WeightKg DECIMAL(18,4) NULL,
        Bodyweight BIT NOT NULL,
        ExerciseCategory NVARCHAR(100) NULL,
        ExerciseName NVARCHAR(100) NULL,
        CandidateCount INT NULL,
        TopProbability FLOAT NULL,
        StartTimeUtc DATETIMEOFFSET(7) NULL,
        DurationSeconds FLOAT NULL,
        WktStepIndex INT NULL,
        CONSTRAINT PK_ExerciseSets PRIMARY KEY (UserId, ActivityId, Position),
        CONSTRAINT FK_ExerciseSets_Activities FOREIGN KEY (UserId, ActivityId)
            REFERENCES dbo.Activities (UserId, ActivityId)
    );
END;
