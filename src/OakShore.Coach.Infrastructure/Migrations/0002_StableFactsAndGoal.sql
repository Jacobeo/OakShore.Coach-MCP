IF COL_LENGTH(N'dbo.AthleteProfiles', N'AvailableEquipment') IS NULL
BEGIN
    ALTER TABLE dbo.AthleteProfiles ALTER COLUMN BodyWeightKg DECIMAL(18, 1) NULL;
    ALTER TABLE dbo.AthleteProfiles ADD
        AvailableEquipment NVARCHAR(MAX) NOT NULL CONSTRAINT DF_AthleteProfiles_AvailableEquipment DEFAULT N'[]',
        IntendedTrainingFrequencyPerWeek INT NULL,
        IntendedTrainingDurationMinutes INT NULL,
        LastingLimitations NVARCHAR(MAX) NOT NULL CONSTRAINT DF_AthleteProfiles_LastingLimitations DEFAULT N'[]',
        GoalDescription NVARCHAR(500) NULL,
        GoalTargetDate DATE NULL;
END;
