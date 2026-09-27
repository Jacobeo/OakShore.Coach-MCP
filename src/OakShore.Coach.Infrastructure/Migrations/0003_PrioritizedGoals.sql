SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.AthleteProfiles', N'GoalsJson') IS NULL
BEGIN
    ALTER TABLE dbo.AthleteProfiles ADD
        GoalsJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_AthleteProfiles_GoalsJson DEFAULT N'[]',
        GoalsTargetDate DATE NULL;
    EXEC sp_executesql N'
        UPDATE profile
        SET GoalsJson = (SELECT profile.GoalDescription AS [Description] FOR JSON PATH),
            GoalsTargetDate = profile.GoalTargetDate
        FROM dbo.AthleteProfiles AS profile
        WHERE profile.GoalDescription IS NOT NULL';
    ALTER TABLE dbo.AthleteProfiles DROP COLUMN GoalDescription, GoalTargetDate;
END;
COMMIT;
