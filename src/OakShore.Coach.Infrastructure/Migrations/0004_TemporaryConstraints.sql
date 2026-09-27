IF OBJECT_ID(N'dbo.Constraints', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Constraints
    (
        UserId NVARCHAR(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ConstraintId BIGINT IDENTITY(1, 1) NOT NULL,
        Description NVARCHAR(500) NOT NULL,
        ValidFrom DATETIMEOFFSET(7) NOT NULL,
        ValidUntil DATETIMEOFFSET(7) NOT NULL,
        CONSTRAINT PK_Constraints PRIMARY KEY (UserId, ConstraintId),
        CONSTRAINT FK_Constraints_AthleteProfiles FOREIGN KEY (UserId) REFERENCES dbo.AthleteProfiles (UserId),
        CONSTRAINT CK_Constraints_ValidPeriod CHECK (ValidFrom < ValidUntil)
    );
    CREATE INDEX IX_Constraints_Active ON dbo.Constraints (UserId, ValidFrom, ValidUntil);
END;
