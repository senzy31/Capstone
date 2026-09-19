-- Schema changes made after the original Joblinkv2 database was created.
-- Safe to re-run: every statement checks before it changes anything.
--
--   sqlcmd -S "(localdb)\MSSQLLocalDB" -d Joblinkv2 -C -i schema-changes.sql

-- Employer accounts store their company name at signup.
IF COL_LENGTH('Users', 'company_name') IS NULL
    ALTER TABLE Users ADD company_name VARCHAR(255) NULL;
GO

-- One row per user: the location / salary / work-setup they prefer.
-- Drives the suitability score on the dashboard's Recommended Jobs.
IF OBJECT_ID('Job_Preferences', 'U') IS NULL
BEGIN
    CREATE TABLE Job_Preferences (
        preference_id      INT IDENTITY(1,1) PRIMARY KEY,
        user_id            INT NOT NULL,
        preferred_location VARCHAR(255) NULL,
        work_arrangement   VARCHAR(20)  NULL,   -- onsite | remote | hybrid
        min_salary        DECIMAL(12,2) NULL,  -- monthly, PHP
        max_salary         DECIMAL(12,2) NULL,  -- monthly, PHP
        is_deleted         BIT NULL DEFAULT 0,
        CONSTRAINT FK_Job_Preferences_Users FOREIGN KEY (user_id) REFERENCES Users(user_id),
        CONSTRAINT UQ_Job_Preferences_User UNIQUE (user_id)
    );
END
GO
