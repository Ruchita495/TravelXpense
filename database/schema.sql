/* =====================================================================
   TravelXpense - SQL Server / Azure SQL schema
   The API creates this automatically on first start (EF Core EnsureCreated).
   This script is here so you can READ the design, run it by hand, or
   hand it to a DBA. It mirrors the EF model exactly.
   ===================================================================== */
IF DB_ID('TravelXpense') IS NULL CREATE DATABASE TravelXpense;
GO
USE TravelXpense;
GO

CREATE TABLE Users (
    Id            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    Email         NVARCHAR(256)    NOT NULL,
    FullName      NVARCHAR(200)    NOT NULL,
    PasswordHash  NVARCHAR(100)    NOT NULL,           -- BCrypt hash, never the password
    Role          NVARCHAR(20)     NOT NULL,           -- Employee | Manager | Admin
    ManagerId     UNIQUEIDENTIFIER NULL,
    CreatedAt     DATETIME2        NOT NULL,
    CONSTRAINT FK_Users_Manager FOREIGN KEY (ManagerId) REFERENCES Users(Id)
);
CREATE UNIQUE INDEX UX_Users_Email ON Users(Email);
CREATE INDEX IX_Users_ManagerId ON Users(ManagerId);

CREATE TABLE Trips (
    Id          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    EmployeeId  UNIQUEIDENTIFIER NOT NULL,
    Destination NVARCHAR(200)    NOT NULL,
    StartDate   DATE             NOT NULL,
    EndDate     DATE             NOT NULL,
    Purpose     NVARCHAR(500)    NOT NULL,
    Status      NVARCHAR(20)     NOT NULL,             -- Active | Completed
    CreatedAt   DATETIME2        NOT NULL,
    CONSTRAINT FK_Trips_Employee FOREIGN KEY (EmployeeId) REFERENCES Users(Id)
);
CREATE INDEX IX_Trips_EmployeeId ON Trips(EmployeeId);

CREATE TABLE Bookings (
    Id          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    TripId      UNIQUEIDENTIFIER NOT NULL,
    Type        NVARCHAR(20)     NOT NULL,
    Provider    NVARCHAR(100)    NOT NULL,
    Reference   NVARCHAR(20)     NOT NULL,
    Description NVARCHAR(300)    NOT NULL,
    Price       DECIMAL(18,2)    NOT NULL,
    Currency    NVARCHAR(3)      NOT NULL,
    Cabin       NVARCHAR(20)     NOT NULL,
    CreatedAt   DATETIME2        NOT NULL,
    CONSTRAINT FK_Bookings_Trip FOREIGN KEY (TripId) REFERENCES Trips(Id) ON DELETE CASCADE
);
CREATE INDEX IX_Bookings_TripId ON Bookings(TripId);

CREATE TABLE ExpenseReports (
    Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    TripId          UNIQUEIDENTIFIER NOT NULL,
    EmployeeId      UNIQUEIDENTIFIER NOT NULL,
    Title           NVARCHAR(200)    NOT NULL,
    Status          NVARCHAR(20)     NOT NULL,         -- Draft | Submitted | PendingApproval | Flagged | Approved | Rejected
    Total           DECIMAL(18,2)    NOT NULL,
    Currency        NVARCHAR(3)      NOT NULL,
    CreatedAt       DATETIME2        NOT NULL,
    SubmittedAt     DATETIME2        NULL,
    ReviewedById    UNIQUEIDENTIFIER NULL,
    ReviewedAt      DATETIME2        NULL,
    ReviewerComment NVARCHAR(1000)   NULL,
    CONSTRAINT FK_Reports_Trip     FOREIGN KEY (TripId)     REFERENCES Trips(Id),
    CONSTRAINT FK_Reports_Employee FOREIGN KEY (EmployeeId) REFERENCES Users(Id)
);
CREATE INDEX IX_Reports_Employee_Status ON ExpenseReports(EmployeeId, Status);
CREATE INDEX IX_Reports_TripId ON ExpenseReports(TripId);

CREATE TABLE ExpenseItems (
    Id               UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    ReportId         UNIQUEIDENTIFIER NOT NULL,
    [Date]           DATE             NOT NULL,
    Merchant         NVARCHAR(200)    NOT NULL,
    [Description]    NVARCHAR(500)    NOT NULL,
    Amount           DECIMAL(18,2)    NOT NULL,
    Currency         NVARCHAR(3)      NOT NULL,
    Category         NVARCHAR(30)     NOT NULL,         -- Uncategorized | Airfare | Lodging | Meals | GroundTransport | Other
    AutoCategorized  BIT              NOT NULL,
    ReceiptUrl       NVARCHAR(500)    NULL,
    CONSTRAINT FK_Items_Report FOREIGN KEY (ReportId) REFERENCES ExpenseReports(Id) ON DELETE CASCADE
);
CREATE INDEX IX_Items_ReportId ON ExpenseItems(ReportId);

CREATE TABLE PolicyRules (
    Id          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    Category    NVARCHAR(30)     NOT NULL,
    MaxAmount   DECIMAL(18,2)    NOT NULL,
    [Description] NVARCHAR(300)  NOT NULL,
    Active      BIT              NOT NULL
);

CREATE TABLE PolicyViolations (
    Id            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    ReportId      UNIQUEIDENTIFIER NOT NULL,
    ExpenseItemId UNIQUEIDENTIFIER NULL,
    Message       NVARCHAR(500)    NOT NULL,
    CreatedAt     DATETIME2        NOT NULL,
    CONSTRAINT FK_Violations_Report FOREIGN KEY (ReportId) REFERENCES ExpenseReports(Id) ON DELETE CASCADE
);
CREATE INDEX IX_Violations_ReportId ON PolicyViolations(ReportId);

-- Idempotency table: one row per queue message already handled.
CREATE TABLE ProcessedMessages (
    MessageId   NVARCHAR(64)  NOT NULL PRIMARY KEY,
    Handler     NVARCHAR(100) NOT NULL,
    ProcessedAt DATETIME2     NOT NULL
);

CREATE TABLE AuditLogs (
    Id         BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserId     UNIQUEIDENTIFIER NULL,
    Action     NVARCHAR(100)    NOT NULL,
    EntityType NVARCHAR(50)     NOT NULL,
    EntityId   NVARCHAR(64)     NOT NULL,
    Details    NVARCHAR(1000)   NULL,
    [At]       DATETIME2        NOT NULL
);

-- Seed: company spending rules
INSERT INTO PolicyRules (Id, Category, MaxAmount, [Description], Active) VALUES
 ('00000000-0000-0000-0000-000000000001','Airfare',        1500.00,'Airfare cap per ticket (economy)',1),
 ('00000000-0000-0000-0000-000000000002','Lodging',         300.00,'Hotel cap per night',1),
 ('00000000-0000-0000-0000-000000000003','Meals',            75.00,'Meal cap per receipt',1),
 ('00000000-0000-0000-0000-000000000004','GroundTransport', 150.00,'Ground transport cap per receipt',1),
 ('00000000-0000-0000-0000-000000000005','Other',           100.00,'Other expenses cap per receipt',1);
GO
