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


-- =====================================================================
-- Apply flow: internal jobs (posted by a JobLink employer) vs external
-- jobs (imported from JSearch, no employer on JobLink).
-- Needs SET QUOTED_IDENTIFIER ON (filtered indexes); run with sqlcmd -I.
-- =====================================================================
SET QUOTED_IDENTIFIER ON;
GO

-- JSearch job ids are ~400 chars, titles/companies run past 100, and the old
-- text/varchar columns silently turn non-Latin characters into '?'.
IF (SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID('Job_Listings') AND name = 'external_job_id') < 900
    ALTER TABLE Job_Listings ALTER COLUMN external_job_id VARCHAR(900) NULL;
GO
IF (SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID('Job_Listings') AND name = 'title') < 600
    ALTER TABLE Job_Listings ALTER COLUMN title NVARCHAR(300) NULL;
GO
IF (SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID('Job_Listings') AND name = 'company') < 600
    ALTER TABLE Job_Listings ALTER COLUMN company NVARCHAR(300) NULL;
GO
IF (SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID('Job_Listings') AND name = 'location') < 600
    ALTER TABLE Job_Listings ALTER COLUMN location NVARCHAR(300) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
           WHERE c.object_id = OBJECT_ID('Job_Listings') AND c.name = 'description' AND t.name = 'text')
    ALTER TABLE Job_Listings ALTER COLUMN description NVARCHAR(MAX) NULL;
GO

-- Job_Listings: where the job came from and how to apply to it.
--   Existing rows (manually logged from the tracker) become 'External' with no link.
IF COL_LENGTH('Job_Listings', 'source') IS NULL
    ALTER TABLE Job_Listings ADD source VARCHAR(10) NOT NULL
        CONSTRAINT DF_Job_Listings_source DEFAULT 'External';            -- Internal | External
IF COL_LENGTH('Job_Listings', 'employer_id') IS NULL
    ALTER TABLE Job_Listings ADD employer_id INT NULL
        CONSTRAINT FK_Job_Listings_Employer FOREIGN KEY REFERENCES Users(user_id);   -- NULL for external jobs
IF COL_LENGTH('Job_Listings', 'apply_url') IS NULL
    ALTER TABLE Job_Listings ADD apply_url NVARCHAR(2000) NULL;
IF COL_LENGTH('Job_Listings', 'apply_is_direct') IS NULL
    ALTER TABLE Job_Listings ADD apply_is_direct BIT NOT NULL
        CONSTRAINT DF_Job_Listings_apply_is_direct DEFAULT 0;
IF COL_LENGTH('Job_Listings', 'publisher') IS NULL
    ALTER TABLE Job_Listings ADD publisher NVARCHAR(200) NULL;           -- e.g. LinkedIn, Indeed
IF COL_LENGTH('Job_Listings', 'apply_options') IS NULL
    ALTER TABLE Job_Listings ADD apply_options NVARCHAR(MAX) NULL;       -- JSearch apply_options JSON array
IF COL_LENGTH('Job_Listings', 'is_expired') IS NULL
    ALTER TABLE Job_Listings ADD is_expired BIT NOT NULL
        CONSTRAINT DF_Job_Listings_is_expired DEFAULT 0;                 -- set when no usable apply link is left
IF COL_LENGTH('Job_Listings', 'latitude') IS NULL
    ALTER TABLE Job_Listings ADD latitude DECIMAL(9,6) NULL;
IF COL_LENGTH('Job_Listings', 'longitude') IS NULL
    ALTER TABLE Job_Listings ADD longitude DECIMAL(9,6) NULL;
GO

IF OBJECT_ID('CK_Job_Listings_source', 'C') IS NULL
    ALTER TABLE Job_Listings ADD CONSTRAINT CK_Job_Listings_source CHECK (
        (source = 'Internal' AND employer_id IS NOT NULL) OR
        (source = 'External' AND employer_id IS NULL));
IF OBJECT_ID('CK_Job_Listings_apply_options_json', 'C') IS NULL
    ALTER TABLE Job_Listings ADD CONSTRAINT CK_Job_Listings_apply_options_json CHECK (
        apply_options IS NULL OR ISJSON(apply_options) = 1);

-- One row per imported job: importing the same JSearch job again updates it.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Job_Listings_Source_ExternalId' AND object_id = OBJECT_ID('Job_Listings'))
    CREATE UNIQUE INDEX UX_Job_Listings_Source_ExternalId
        ON Job_Listings (source_api, external_job_id)
        WHERE external_job_id IS NOT NULL AND is_deleted = 0;
GO

-- Applications: which kind of apply this was, and when the user was sent away / confirmed.
--   Existing rows (tracker-logged) become 'External' - they never count toward the
--   internal apply limit and the employer never sees them.
IF COL_LENGTH('Applications', 'application_type') IS NULL
    ALTER TABLE Applications ADD application_type VARCHAR(10) NOT NULL
        CONSTRAINT DF_Applications_application_type DEFAULT 'External';  -- Internal | External
IF COL_LENGTH('Applications', 'redirected_at') IS NULL
    ALTER TABLE Applications ADD redirected_at DATETIME NULL;
IF COL_LENGTH('Applications', 'confirmed_at') IS NULL
    ALTER TABLE Applications ADD confirmed_at DATETIME NULL;
GO

-- Allowed statuses.
--   Internal: Submitted, Viewed, Shortlisted, Rejected
--   External: Redirected, Applied Externally  (+ the tracker's own manual-log
--             statuses Applied / Under Review / Interview / Offer / Rejected,
--             which existing rows already use)
IF OBJECT_ID('CK_Applications_type_status', 'C') IS NULL
    ALTER TABLE Applications ADD CONSTRAINT CK_Applications_type_status CHECK (
        (application_type = 'Internal' AND status IN ('Submitted', 'Viewed', 'Shortlisted', 'Rejected'))
        OR
        (application_type = 'External' AND (status IS NULL OR status IN
            ('Redirected', 'Applied Externally', 'Applied', 'Under Review', 'Interview', 'Offer', 'Rejected'))));

-- A user has at most one live application per job (makes "reuse it" race-proof).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Applications_User_Job_Active' AND object_id = OBJECT_ID('Applications'))
    CREATE UNIQUE INDEX UX_Applications_User_Job_Active
        ON Applications (user_id, job_id) WHERE is_deleted = 0;

-- Speeds up the rolling-24h internal apply limit.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Applications_User_Type_AppliedAt' AND object_id = OBJECT_ID('Applications'))
    CREATE INDEX IX_Applications_User_Type_AppliedAt
        ON Applications (user_id, application_type, applied_at);
GO
