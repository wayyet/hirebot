CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE "AppUsers" (
    "Id" character varying(36) NOT NULL,
    "ExternalUserId" character varying(256) NOT NULL,
    "TenantId" character varying(128),
    "Username" character varying(128) NOT NULL,
    "DisplayName" character varying(256) NOT NULL,
    "FamilyName" character varying(256),
    "GivenName" character varying(256),
    "Email" character varying(256) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "LastSeenAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AppUsers" PRIMARY KEY ("Id")
);

CREATE TABLE "EvaluationSessions" (
    "Id" uuid NOT NULL,
    "SessionId" character varying(120) NOT NULL,
    "OwnerSubject" character varying(120) NOT NULL,
    "TenantId" character varying(128) NOT NULL,
    "EmployeeId" character varying(120) NOT NULL,
    "TargetHireId" character varying(120) NOT NULL,
    "TargetSandboxId" character varying(120) NOT NULL,
    "EvaluatorHireId" character varying(120) NOT NULL,
    "EvaluatorSandboxId" character varying(120) NOT NULL,
    "Status" character varying(40) NOT NULL,
    "Iteration" integer NOT NULL,
    "LastError" character varying(1024),
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_EvaluationSessions" PRIMARY KEY ("Id")
);

CREATE TABLE "EvaluationWorkspaceStates" (
    "Id" uuid NOT NULL,
    "OwnerSubject" character varying(120) NOT NULL,
    "TenantId" character varying(128) NOT NULL,
    "EmployeeId" character varying(120) NOT NULL,
    "PayloadJson" text NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_EvaluationWorkspaceStates" PRIMARY KEY ("Id")
);

CREATE TABLE "HiringArtifacts" (
    "ArtifactId" uuid NOT NULL,
    "SessionId" character varying(64) NOT NULL,
    "TenantId" character varying(128) NOT NULL,
    "Kind" character varying(32) NOT NULL,
    "LogicalPath" character varying(1024) NOT NULL,
    "FileName" character varying(512) NOT NULL,
    "SizeBytes" bigint NOT NULL,
    "Sha256" character varying(64) NOT NULL,
    "PackageId" character varying(64),
    "StoragePath" character varying(1024) NOT NULL,
    "IsFinal" boolean NOT NULL,
    "IsArchived" boolean NOT NULL,
    "UploadedAtUtc" timestamp with time zone NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    "DeletedBy" character varying(256),
    CONSTRAINT "PK_HiringArtifacts" PRIMARY KEY ("ArtifactId")
);

CREATE TABLE "HiringAuditLogs" (
    "AuditId" uuid NOT NULL,
    "SessionId" character varying(64) NOT NULL,
    "TenantId" character varying(128) NOT NULL,
    "HireId" character varying(64) NOT NULL,
    "ArtifactId" character varying(64),
    "BeforeSha256" character varying(64),
    "AfterSha256" character varying(64),
    "Action" character varying(64) NOT NULL,
    "Actor" character varying(256) NOT NULL,
    "Ip" character varying(64),
    "TimestampUtc" timestamp with time zone NOT NULL,
    "DetailJson" character varying(2048),
    CONSTRAINT "PK_HiringAuditLogs" PRIMARY KEY ("AuditId")
);

CREATE TABLE "HiringExternalConfigs" (
    "HireId" character varying(64) NOT NULL,
    "TenantId" character varying(128),
    "ConfigJson" text NOT NULL DEFAULT '{}',
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedBy" character varying(256),
    CONSTRAINT "PK_HiringExternalConfigs" PRIMARY KEY ("HireId")
);

CREATE TABLE "HiringMaterialFiles" (
    "MaterialFileId" uuid NOT NULL,
    "HireId" character varying(64) NOT NULL,
    "SessionId" character varying(64) NOT NULL,
    "RelativePath" character varying(1024) NOT NULL,
    "OriginalFileName" character varying(512) NOT NULL,
    "StoragePath" character varying(1024) NOT NULL,
    "Format" character varying(32) NOT NULL,
    "MimeType" character varying(120),
    "SizeBytes" bigint NOT NULL,
    "Sha256" character varying(64) NOT NULL,
    "RequestedCategoryTitle" character varying(160),
    "WorkspaceRelativePath" character varying(1024),
    "TenantId" character varying(128) NOT NULL,
    "OperatorId" character varying(128) NOT NULL,
    "UploadedBy" character varying(256) NOT NULL,
    "UploadedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_HiringMaterialFiles" PRIMARY KEY ("MaterialFileId")
);

CREATE TABLE "HiringSessions" (
    "SessionId" character varying(64) NOT NULL,
    "HireId" character varying(64) NOT NULL,
    "TemplateId" character varying(128) NOT NULL,
    "PackageId" character varying(256),
    "PackageVersion" character varying(64),
    "PackageHash" character varying(64),
    "SourceZipSha256" character varying(64),
    "SourceZipStoragePath" character varying(1024),
    "SourceZipSizeBytes" bigint,
    "OwnerSubject" character varying(256) NOT NULL,
    "TenantId" character varying(128),
    "OperatorId" character varying(128) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    "DeletedBy" character varying(256),
    CONSTRAINT "PK_HiringSessions" PRIMARY KEY ("SessionId")
);

CREATE TABLE "HiringSkillLinkConfigs" (
    "HireId" character varying(64) NOT NULL,
    "TenantId" character varying(128),
    "ConfigJson" text NOT NULL DEFAULT '{}',
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedBy" character varying(256),
    CONSTRAINT "PK_HiringSkillLinkConfigs" PRIMARY KEY ("HireId")
);

CREATE TABLE "HiringStageProgresses" (
    "HireId" character varying(64) NOT NULL,
    "TenantId" character varying(128),
    "CurrentStage" character varying(40) NOT NULL,
    "PackagingTestCasesStatus" character varying(40),
    "StageOverridesJson" jsonb,
    "DownstreamRunsJson" jsonb,
    "UploadedFilesJson" jsonb,
    "PackageStructureJson" jsonb,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedBy" character varying(256),
    CONSTRAINT "PK_HiringStageProgresses" PRIMARY KEY ("HireId")
);

CREATE TABLE "HiringStructuredData" (
    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
    "HireId" character varying(64) NOT NULL,
    "TenantId" character varying(128),
    "FieldKey" character varying(256) NOT NULL,
    "FieldValue" text,
    "CollectedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_HiringStructuredData" PRIMARY KEY ("Id")
);

CREATE TABLE "Instances" (
    "InstanceId" character varying(120) NOT NULL,
    "TenantId" character varying(128) NOT NULL,
    "InstanceType" character varying(40) NOT NULL,
    "Status" character varying(40) NOT NULL,
    "BasedOnTemplateId" character varying(128),
    "HireId" character varying(64),
    "FromInstanceId" character varying(120),
    "ActiveBranchId" character varying(120),
    "EvalReportId" character varying(120),
    "FinalPackageId" character varying(64),
    "OwnerUserId" character varying(256) NOT NULL,
    "DepartmentId" character varying(128) NOT NULL,
    "CurrentVersion" character varying(80) NOT NULL,
    "RuntimeSnapshotJson" text,
    "Description" text,
    "DescribeDocument" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Instances" PRIMARY KEY ("InstanceId")
);

CREATE TABLE "SandboxInstances" (
    "Id" uuid NOT NULL,
    "SandboxId" character varying(120) NOT NULL,
    "ScopeType" character varying(40) NOT NULL,
    "ScopeKey" character varying(160) NOT NULL,
    "SandboxRole" character varying(80) NOT NULL,
    "ProvisioningMode" character varying(40) NOT NULL,
    "OwnerSubject" character varying(256) NOT NULL,
    "TenantId" character varying(128) NOT NULL,
    "OperatorId" character varying(128) NOT NULL,
    "State" character varying(80) NOT NULL,
    "GatewayEndpoint" character varying(512),
    "ExpiresAtUtc" timestamp with time zone,
    "LastError" character varying(1024),
    "UseCase" character varying(200),
    "TemplateId" character varying(128),
    "IsInitialized" boolean NOT NULL,
    "Metadata" jsonb,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SandboxInstances" PRIMARY KEY ("Id")
);

CREATE TABLE "EvaluationAssets" (
    "Id" uuid NOT NULL,
    "TenantId" character varying(128),
    "SessionEntityId" uuid NOT NULL,
    "AssetType" character varying(40) NOT NULL,
    "RelatedKey" character varying(160),
    "RelativePath" character varying(512) NOT NULL,
    "PublicUrl" character varying(512) NOT NULL,
    "MimeType" character varying(120) NOT NULL,
    "Size" bigint NOT NULL,
    "ContentHash" character varying(128) NOT NULL,
    "SourceType" character varying(40) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_EvaluationAssets" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_EvaluationAssets_EvaluationSessions_SessionEntityId" FOREIGN KEY ("SessionEntityId") REFERENCES "EvaluationSessions" ("Id") ON DELETE CASCADE
);

CREATE TABLE "SandboxSessions" (
    "Id" uuid NOT NULL,
    "SandboxInstanceEntityId" uuid,
    "SessionId" character varying(120) NOT NULL,
    "ScopeType" character varying(160) NOT NULL,
    "ScopeKey" character varying(160) NOT NULL,
    "SandboxRole" character varying(80) NOT NULL,
    "SessionKey" character varying(160) NOT NULL,
    "ChannelId" character varying(120),
    "SenderId" character varying(120),
    "TenantId" character varying(128) NOT NULL,
    "OwnerSubject" character varying(256) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SandboxSessions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SandboxSessions_SandboxInstances_SandboxInstanceEntityId" FOREIGN KEY ("SandboxInstanceEntityId") REFERENCES "SandboxInstances" ("Id") ON DELETE SET NULL
);

CREATE TABLE "EvaluationReports" (
    "Id" uuid NOT NULL,
    "TenantId" character varying(128),
    "SessionEntityId" uuid NOT NULL,
    "Iteration" integer NOT NULL,
    "OverallScore" numeric(6,2) NOT NULL,
    "Passed" boolean NOT NULL,
    "DimensionScoresJson" text NOT NULL,
    "SummaryJson" text NOT NULL,
    "ReportJsonAssetId" uuid,
    "ReportHtmlAssetId" uuid,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_EvaluationReports" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_EvaluationReports_EvaluationAssets_ReportHtmlAssetId" FOREIGN KEY ("ReportHtmlAssetId") REFERENCES "EvaluationAssets" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_EvaluationReports_EvaluationAssets_ReportJsonAssetId" FOREIGN KEY ("ReportJsonAssetId") REFERENCES "EvaluationAssets" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_EvaluationReports_EvaluationSessions_SessionEntityId" FOREIGN KEY ("SessionEntityId") REFERENCES "EvaluationSessions" ("Id") ON DELETE CASCADE
);

CREATE TABLE "SandboxAssets" (
    "Id" uuid NOT NULL,
    "TenantId" character varying(128),
    "SandboxInstanceEntityId" uuid,
    "SandboxSessionEntityId" uuid,
    "MediaId" character varying(120) NOT NULL,
    "Url" character varying(512) NOT NULL,
    "FileName" character varying(512) NOT NULL,
    "MimeType" character varying(120) NOT NULL,
    "SizeBytes" bigint NOT NULL,
    "ContentHash" character varying(128),
    "StoragePath" character varying(1024),
    "AssetRole" character varying(80) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_SandboxAssets" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SandboxAssets_SandboxInstances_SandboxInstanceEntityId" FOREIGN KEY ("SandboxInstanceEntityId") REFERENCES "SandboxInstances" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_SandboxAssets_SandboxSessions_SandboxSessionEntityId" FOREIGN KEY ("SandboxSessionEntityId") REFERENCES "SandboxSessions" ("Id") ON DELETE SET NULL
);

CREATE INDEX "IX_AppUsers_ExternalUserId" ON "AppUsers" ("ExternalUserId");

CREATE INDEX "IX_AppUsers_TenantId" ON "AppUsers" ("TenantId");

CREATE UNIQUE INDEX "IX_AppUsers_TenantId_ExternalUserId" ON "AppUsers" ("TenantId", "ExternalUserId");

CREATE INDEX "IX_AppUsers_TenantId_Username" ON "AppUsers" ("TenantId", "Username");

CREATE INDEX "IX_EvaluationAssets_RelativePath" ON "EvaluationAssets" ("RelativePath");

CREATE INDEX "IX_EvaluationAssets_SessionEntityId_AssetType" ON "EvaluationAssets" ("SessionEntityId", "AssetType");

CREATE INDEX "IX_EvaluationAssets_TenantId" ON "EvaluationAssets" ("TenantId");

CREATE INDEX "IX_EvaluationReports_ReportHtmlAssetId" ON "EvaluationReports" ("ReportHtmlAssetId");

CREATE INDEX "IX_EvaluationReports_ReportJsonAssetId" ON "EvaluationReports" ("ReportJsonAssetId");

CREATE INDEX "IX_EvaluationReports_SessionEntityId_Iteration" ON "EvaluationReports" ("SessionEntityId", "Iteration");

CREATE INDEX "IX_EvaluationReports_TenantId" ON "EvaluationReports" ("TenantId");

CREATE UNIQUE INDEX "IX_EvaluationSessions_SessionId" ON "EvaluationSessions" ("SessionId");

CREATE INDEX "IX_EvaluationSessions_TenantId_EvaluatorHireId_TargetHireId" ON "EvaluationSessions" ("TenantId", "EvaluatorHireId", "TargetHireId");

CREATE INDEX "IX_EvaluationSessions_TenantId_OwnerSubject_EmployeeId_Updated~" ON "EvaluationSessions" ("TenantId", "OwnerSubject", "EmployeeId", "UpdatedAtUtc");

CREATE UNIQUE INDEX "IX_EvaluationWorkspaceStates_TenantId_OwnerSubject_EmployeeId" ON "EvaluationWorkspaceStates" ("TenantId", "OwnerSubject", "EmployeeId");

CREATE INDEX "IX_EvaluationWorkspaceStates_UpdatedAtUtc" ON "EvaluationWorkspaceStates" ("UpdatedAtUtc");

CREATE INDEX "IX_HiringArtifacts_TenantId_SessionId_IsFinal" ON "HiringArtifacts" ("TenantId", "SessionId", "IsFinal");

CREATE UNIQUE INDEX "IX_HiringArtifacts_TenantId_SessionId_Kind_LogicalPath" ON "HiringArtifacts" ("TenantId", "SessionId", "Kind", "LogicalPath");

CREATE INDEX "IX_HiringArtifacts_UploadedAtUtc" ON "HiringArtifacts" ("UploadedAtUtc");

CREATE INDEX "IX_HiringAuditLogs_TenantId_HireId_TimestampUtc" ON "HiringAuditLogs" ("TenantId", "HireId", "TimestampUtc");

CREATE INDEX "IX_HiringAuditLogs_TenantId_SessionId_TimestampUtc" ON "HiringAuditLogs" ("TenantId", "SessionId", "TimestampUtc");

CREATE INDEX "IX_HiringExternalConfigs_TenantId_UpdatedAtUtc" ON "HiringExternalConfigs" ("TenantId", "UpdatedAtUtc");

CREATE INDEX "IX_HiringMaterialFiles_HireId_SessionId_UploadedAtUtc" ON "HiringMaterialFiles" ("HireId", "SessionId", "UploadedAtUtc");

CREATE UNIQUE INDEX "IX_HiringMaterialFiles_SessionId_RelativePath" ON "HiringMaterialFiles" ("SessionId", "RelativePath");

CREATE INDEX "IX_HiringMaterialFiles_Sha256" ON "HiringMaterialFiles" ("Sha256");

CREATE INDEX "IX_HiringSessions_CreatedAtUtc" ON "HiringSessions" ("CreatedAtUtc");

CREATE UNIQUE INDEX "IX_HiringSessions_HireId" ON "HiringSessions" ("HireId");

CREATE INDEX "IX_HiringSkillLinkConfigs_TenantId_UpdatedAtUtc" ON "HiringSkillLinkConfigs" ("TenantId", "UpdatedAtUtc");

CREATE INDEX "IX_HiringStageProgresses_TenantId_UpdatedAtUtc" ON "HiringStageProgresses" ("TenantId", "UpdatedAtUtc");

CREATE INDEX "IX_HiringStructuredData_HireId" ON "HiringStructuredData" ("HireId");

CREATE INDEX "IX_HiringStructuredData_HireId_FieldKey" ON "HiringStructuredData" ("HireId", "FieldKey");

CREATE INDEX "IX_Instances_BasedOnTemplateId" ON "Instances" ("BasedOnTemplateId");

CREATE INDEX "IX_Instances_FromInstanceId" ON "Instances" ("FromInstanceId");

CREATE INDEX "IX_Instances_OwnerUserId_InstanceType_Status" ON "Instances" ("OwnerUserId", "InstanceType", "Status");

CREATE INDEX "IX_Instances_TenantId_DepartmentId_InstanceType_Status" ON "Instances" ("TenantId", "DepartmentId", "InstanceType", "Status");

CREATE UNIQUE INDEX "IX_SandboxAssets_MediaId" ON "SandboxAssets" ("MediaId");

CREATE INDEX "IX_SandboxAssets_SandboxInstanceEntityId_CreatedAtUtc" ON "SandboxAssets" ("SandboxInstanceEntityId", "CreatedAtUtc");

CREATE INDEX "IX_SandboxAssets_SandboxSessionEntityId" ON "SandboxAssets" ("SandboxSessionEntityId");

CREATE INDEX "IX_SandboxAssets_TenantId" ON "SandboxAssets" ("TenantId");

CREATE INDEX "IX_SandboxInstances_OwnerSubject_ScopeType_ScopeKey_SandboxRole" ON "SandboxInstances" ("OwnerSubject", "ScopeType", "ScopeKey", "SandboxRole");

CREATE INDEX "IX_SandboxInstances_OwnerSubject_State_UpdatedAtUtc" ON "SandboxInstances" ("OwnerSubject", "State", "UpdatedAtUtc");

CREATE INDEX "IX_SandboxInstances_OwnerSubject_TemplateId_SandboxRole_State" ON "SandboxInstances" ("OwnerSubject", "TemplateId", "SandboxRole", "State");

CREATE UNIQUE INDEX "IX_SandboxInstances_SandboxId" ON "SandboxInstances" ("SandboxId");

CREATE INDEX "IX_SandboxSessions_SandboxInstanceEntityId" ON "SandboxSessions" ("SandboxInstanceEntityId");

CREATE UNIQUE INDEX "IX_SandboxSessions_SessionId" ON "SandboxSessions" ("SessionId");

CREATE UNIQUE INDEX "IX_SandboxSessions_TenantId_OwnerSubject_ScopeType_ScopeKey_Sa~" ON "SandboxSessions" ("TenantId", "OwnerSubject", "ScopeType", "ScopeKey", "SandboxRole", "SessionKey");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260614153510_InitialCreate', '10.0.7');

COMMIT;

