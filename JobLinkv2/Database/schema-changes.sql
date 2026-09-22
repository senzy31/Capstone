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


-- =====================================================================
-- Subscriptions: the two-tier plan for JOB SEEKERS (Free / Premium).
-- Payments are simulated - there is no payment gateway.
--
-- Its own table, not columns on Users: nothing that reads or writes an
-- account (the User endpoints, UserModel) knows this table exists, so a
-- plan can't be changed through them by construction. A user with no row
-- is on the Free plan.
--
-- A user is Premium only while [plan] = 'Premium' AND premium_until > now (plan is a
-- reserved word in T-SQL, so it is always written [plan]).
-- When premium_until passes, nothing has to run: the app checks the date
-- on every request, so the row can keep saying 'Premium' and still mean
-- Free. cancelled_at means "don't renew" - Premium lasts until premium_until.
-- All times are UTC.
-- =====================================================================
IF OBJECT_ID('Subscriptions', 'U') IS NULL
    CREATE TABLE Subscriptions (
        user_id            INT         NOT NULL
            CONSTRAINT PK_Subscriptions PRIMARY KEY
            CONSTRAINT FK_Subscriptions_User FOREIGN KEY REFERENCES Users(user_id),
        [plan]             VARCHAR(10) NOT NULL CONSTRAINT DF_Subscriptions_plan DEFAULT 'Free',
        plan_billing       VARCHAR(10) NULL,        -- Monthly | Quarterly | Annual (NULL on Free)
        premium_started_at DATETIME    NULL,
        premium_until      DATETIME    NULL,
        cancelled_at       DATETIME    NULL,
        updated_at         DATETIME    NOT NULL CONSTRAINT DF_Subscriptions_updated DEFAULT GETUTCDATE(),
        CONSTRAINT CK_Subscriptions_plan CHECK ([plan] IN ('Free', 'Premium')),
        CONSTRAINT CK_Subscriptions_billing CHECK (plan_billing IS NULL OR plan_billing IN ('Monthly', 'Quarterly', 'Annual')),
        -- Premium always says how it is billed and for how long; Free has no billing.
        CONSTRAINT CK_Subscriptions_shape CHECK (
            ([plan] = 'Free' AND plan_billing IS NULL)
            OR
            ([plan] = 'Premium' AND plan_billing IS NOT NULL AND premium_started_at IS NOT NULL AND premium_until IS NOT NULL))
    );
GO

-- =====================================================================
-- Priority Application (Premium): whether the applicant was Premium at the
-- moment they applied. Internal applications only - an external redirect
-- never has a priority. The suitability score never depends on it; it only
-- breaks ties when an employer's applicants are ranked.
-- =====================================================================
IF COL_LENGTH('Applications', 'is_priority') IS NULL
    ALTER TABLE Applications ADD is_priority BIT NOT NULL
        CONSTRAINT DF_Applications_is_priority DEFAULT 0;
GO
IF OBJECT_ID('CK_Applications_priority_internal', 'C') IS NULL
    ALTER TABLE Applications ADD CONSTRAINT CK_Applications_priority_internal CHECK (
        is_priority = 0 OR application_type = 'Internal');
GO


-- =====================================================================
-- Paid employer job posting. Payments are SIMULATED (same DemoCheckout
-- pattern as Subscriptions) - there is no payment gateway.
--
-- Job_Post_Purchases is an append-only ledger, one row per purchase, not a
-- single running balance: it doubles as purchase history for the employer's
-- "My Job Posts" page, and it keeps "post" credits (Single/Bundle5) and
-- "renewal" credits (Renewal) apart, since a renewal never grants a new
-- post. Spending a credit decrements credits_remaining on the oldest row of
-- that kind that still has one (FIFO), the same per-user sp_getapplock
-- pattern SqlSubscriptionStore already uses for Subscriptions.
-- =====================================================================
IF OBJECT_ID('Job_Post_Purchases', 'U') IS NULL
    CREATE TABLE Job_Post_Purchases (
        purchase_id       INT IDENTITY(1,1) PRIMARY KEY,
        employer_id       INT NOT NULL
            CONSTRAINT FK_Job_Post_Purchases_Employer FOREIGN KEY REFERENCES Users(user_id),
        package           VARCHAR(10) NOT NULL,   -- Single | Bundle5 | Renewal
        credit_kind       VARCHAR(10) NOT NULL,   -- Post | Renewal
        amount_php        DECIMAL(10,2) NOT NULL,
        credits_granted   INT NOT NULL,
        credits_remaining INT NOT NULL,
        purchased_at      DATETIME NOT NULL CONSTRAINT DF_Job_Post_Purchases_purchased_at DEFAULT GETUTCDATE(),
        CONSTRAINT CK_Job_Post_Purchases_package CHECK (package IN ('Single', 'Bundle5', 'Renewal')),
        CONSTRAINT CK_Job_Post_Purchases_credit_kind CHECK (credit_kind IN ('Post', 'Renewal')),
        CONSTRAINT CK_Job_Post_Purchases_credits CHECK (credits_remaining >= 0 AND credits_remaining <= credits_granted)
    );
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Job_Post_Purchases_Employer_Kind' AND object_id = OBJECT_ID('Job_Post_Purchases'))
    CREATE INDEX IX_Job_Post_Purchases_Employer_Kind ON Job_Post_Purchases (employer_id, credit_kind, purchased_at);
GO

-- Job_Listings: employer-posted (Internal) jobs get real posting fields.
-- Existing rows (External, imported or manually logged) default to Draft -
-- harmless, since status is never read for anything but Internal jobs.
IF COL_LENGTH('Job_Listings', 'status') IS NULL
    ALTER TABLE Job_Listings ADD status VARCHAR(10) NOT NULL
        CONSTRAINT DF_Job_Listings_status DEFAULT 'Draft';       -- Draft | Active | Closed | Expired
IF COL_LENGTH('Job_Listings', 'salary_min') IS NULL
    ALTER TABLE Job_Listings ADD salary_min DECIMAL(12,2) NULL;  -- monthly, PHP - same shape as Job_Preferences
IF COL_LENGTH('Job_Listings', 'salary_max') IS NULL
    ALTER TABLE Job_Listings ADD salary_max DECIMAL(12,2) NULL;
IF COL_LENGTH('Job_Listings', 'work_setup') IS NULL
    ALTER TABLE Job_Listings ADD work_setup VARCHAR(20) NULL;    -- onsite | remote | hybrid
IF COL_LENGTH('Job_Listings', 'job_type') IS NULL
    ALTER TABLE Job_Listings ADD job_type VARCHAR(20) NULL;      -- FULLTIME | PARTTIME | CONTRACTOR | INTERN
IF COL_LENGTH('Job_Listings', 'published_at') IS NULL
    ALTER TABLE Job_Listings ADD published_at DATETIME NULL;
IF COL_LENGTH('Job_Listings', 'expires_at') IS NULL
    ALTER TABLE Job_Listings ADD expires_at DATETIME NULL;
GO

IF OBJECT_ID('CK_Job_Listings_status', 'C') IS NULL
    ALTER TABLE Job_Listings ADD CONSTRAINT CK_Job_Listings_status CHECK (
        status IN ('Draft', 'Active', 'Closed', 'Expired'));
IF OBJECT_ID('CK_Job_Listings_salary_range', 'C') IS NULL
    ALTER TABLE Job_Listings ADD CONSTRAINT CK_Job_Listings_salary_range CHECK (
        salary_min IS NULL OR salary_max IS NULL OR salary_min <= salary_max);
IF OBJECT_ID('CK_Job_Listings_work_setup', 'C') IS NULL
    ALTER TABLE Job_Listings ADD CONSTRAINT CK_Job_Listings_work_setup CHECK (
        work_setup IS NULL OR work_setup IN ('onsite', 'remote', 'hybrid'));
IF OBJECT_ID('CK_Job_Listings_job_type', 'C') IS NULL
    ALTER TABLE Job_Listings ADD CONSTRAINT CK_Job_Listings_job_type CHECK (
        job_type IS NULL OR job_type IN ('FULLTIME', 'PARTTIME', 'CONTRACTOR', 'INTERN'));
GO

-- Required skills on an employer-posted job - the same shared Skills catalog
-- resumes use, so missing-skills analysis can compare the two lists later.
-- Removing a skill from a posting keeps the row (like Resume_Skills): flip
-- is_deleted back to 0 to re-add it instead of a duplicate key error.
IF OBJECT_ID('Job_Listing_Skills', 'U') IS NULL
    CREATE TABLE Job_Listing_Skills (
        job_id     INT NOT NULL CONSTRAINT FK_Job_Listing_Skills_Job FOREIGN KEY REFERENCES Job_Listings(job_id),
        skill_id   INT NOT NULL CONSTRAINT FK_Job_Listing_Skills_Skill FOREIGN KEY REFERENCES Skills(skill_id),
        is_deleted BIT NOT NULL CONSTRAINT DF_Job_Listing_Skills_is_deleted DEFAULT 0,
        CONSTRAINT PK_Job_Listing_Skills PRIMARY KEY (job_id, skill_id)
    );
GO


-- =====================================================================
-- Profile pictures (job seekers and employers). The bytes live in the
-- database, not on disk - there is no wwwroot/static-file serving in this
-- app yet, and this reuses the same "stream bytes with a content type"
-- shape the resume PDF/DOCX export already uses.
--
-- photo_key is a random, unguessable id, regenerated on every upload -
-- GET /api/Profile/photo/{photoKey} needs no login (so <img src> just
-- works), but there is deliberately no endpoint that maps a user id to a
-- photo_key for anyone but that profile's own owner. An old key stops
-- working the moment a new photo is uploaded.
-- =====================================================================
IF COL_LENGTH('Profiles', 'photo') IS NULL
    ALTER TABLE Profiles ADD photo VARBINARY(MAX) NULL;
IF COL_LENGTH('Profiles', 'photo_content_type') IS NULL
    ALTER TABLE Profiles ADD photo_content_type VARCHAR(20) NULL;
IF COL_LENGTH('Profiles', 'photo_key') IS NULL
    ALTER TABLE Profiles ADD photo_key UNIQUEIDENTIFIER NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Profiles_PhotoKey' AND object_id = OBJECT_ID('Profiles'))
    CREATE UNIQUE INDEX UX_Profiles_PhotoKey ON Profiles (photo_key) WHERE photo_key IS NOT NULL;
GO


-- =====================================================================
-- Notifications: what kind, and where clicking one should go. NULL on
-- every row created before this - the bell only started reading them.
-- =====================================================================
IF COL_LENGTH('Notifications', 'type') IS NULL
    ALTER TABLE Notifications ADD type VARCHAR(40) NULL;
IF COL_LENGTH('Notifications', 'link') IS NULL
    ALTER TABLE Notifications ADD link VARCHAR(255) NULL;
GO

IF OBJECT_ID('CK_Notifications_type', 'C') IS NULL
    ALTER TABLE Notifications ADD CONSTRAINT CK_Notifications_type CHECK (
        type IS NULL OR type IN (
            'NewApplication', 'ApplicationStatusChanged',
            'PremiumActivated', 'PremiumExpiringSoon', 'PremiumExpired',
            'ConfirmExternalReminder',
            'JobPublished', 'JobExpiringSoon', 'JobExpired', 'PurchaseConfirmed'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Notifications_User_Type_Created' AND object_id = OBJECT_ID('Notifications'))
    CREATE INDEX IX_Notifications_User_Type_Created ON Notifications (user_id, type, created_at);
GO
