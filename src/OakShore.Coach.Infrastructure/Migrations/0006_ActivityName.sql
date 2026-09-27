IF COL_LENGTH(N'dbo.Activities', N'ActivityName') IS NULL
BEGIN
    ALTER TABLE dbo.Activities ADD ActivityName NVARCHAR(500) NULL;
END;
