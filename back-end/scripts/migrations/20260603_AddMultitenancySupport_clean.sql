-- ======================================================================
-- 多租户支持迁移脚本（幂等版本）
-- 迁移ID: 20260603165207_AddMultitenancySupport
-- 日期: 2026-06-03
-- 说明: 为HireBot项目添加完整的多租户支持
-- 特性: 使用 IF NOT EXISTS 检查，可安全重复执行
-- ======================================================================

-- ============================================================
-- 阶段1：删除旧索引
-- ============================================================

DROP INDEX IF EXISTS "IX_SandboxSessions_OwnerSubject_ScopeType_ScopeKey_SandboxRole~";
DROP INDEX IF EXISTS "IX_HiringRuntimeStates_SessionId";
DROP INDEX IF EXISTS "IX_HiringRuntimeStates_UpdatedAtUtc";
DROP INDEX IF EXISTS "IX_HiringAuditLogs_HireId_TimestampUtc";
DROP INDEX IF EXISTS "IX_HiringAuditLogs_SessionId_TimestampUtc";
DROP INDEX IF EXISTS "IX_HiringArtifactUploads_SessionId_Kind_LogicalPath_CompletedA~";
DROP INDEX IF EXISTS "IX_HiringArtifacts_SessionId_IsFinal";
DROP INDEX IF EXISTS "IX_HiringArtifacts_SessionId_Kind_LogicalPath";
DROP INDEX IF EXISTS "IX_EvaluationSessions_EvaluatorHireId_TargetHireId";
DROP INDEX IF EXISTS "IX_EvaluationSessions_OwnerSubject_EmployeeId_UpdatedAtUtc";

-- ============================================================
-- 阶段2：修改 EvaluationWorkspaceStates 主键
-- ============================================================

ALTER TABLE "EvaluationWorkspaceStates" DROP CONSTRAINT IF EXISTS "PK_EvaluationWorkspaceStates";

-- ============================================================
-- 阶段3：添加 TenantId 字段
-- ============================================================

-- SandboxSessions（必需）
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'SandboxSessions' AND column_name = 'TenantId'
    ) THEN
        ALTER TABLE "SandboxSessions" ADD COLUMN "TenantId" character varying(128) NOT NULL DEFAULT 'default';
    END IF;
END $$;

-- SandboxAssets（可选）
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'SandboxAssets' AND column_name = 'TenantId'
    ) THEN
        ALTER TABLE "SandboxAssets" ADD COLUMN "TenantId" character varying(128);
    END IF;
END $$;

-- HiringSessions（改为可空）
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'HiringSessions' AND column_name = 'TenantId' AND is_nullable = 'NO'
    ) THEN
        ALTER TABLE "HiringSessions" ALTER COLUMN "TenantId" DROP NOT NULL;
    END IF;
END $$;

-- HiringRuntimeStates（必需）
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'HiringRuntimeStates' AND column_name = 'TenantId'
    ) THEN
        ALTER TABLE "HiringRuntimeStates" ADD COLUMN "TenantId" character varying(128) NOT NULL DEFAULT 'default';
    END IF;
END $$;

-- HiringAuditLogs（必需）
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'HiringAuditLogs' AND column_name = 'TenantId'
    ) THEN
        ALTER TABLE "HiringAuditLogs" ADD COLUMN "TenantId" character varying(128) NOT NULL DEFAULT 'default';
    END IF;
END $$;

-- HiringArtifactUploads（必需）
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'HiringArtifactUploads' AND column_name = 'TenantId'
    ) THEN
        ALTER TABLE "HiringArtifactUploads" ADD COLUMN "TenantId" character varying(128) NOT NULL DEFAULT 'default';
    END IF;
END $$;

-- HiringArtifactUploadParts（可选）
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'HiringArtifactUploadParts' AND column_name = 'TenantId'
    ) THEN
        ALTER TABLE "HiringArtifactUploadParts" ADD COLUMN "TenantId" character varying(128);
    END IF;
END $$;

-- HiringArtifacts（必需）
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'HiringArtifacts' AND column_name = 'TenantId'
    ) THEN
        ALTER TABLE "HiringArtifacts" ADD COLUMN "TenantId" character varying(128) NOT NULL DEFAULT 'default';
    END IF;
END $$;

-- EvaluationWorkspaceStates（添加主键和TenantId）
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'EvaluationWorkspaceStates' AND column_name = 'Id'
    ) THEN
        ALTER TABLE "EvaluationWorkspaceStates" ADD COLUMN "Id" uuid NOT NULL DEFAULT gen_random_uuid();
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'EvaluationWorkspaceStates' AND column_name = 'TenantId'
    ) THEN
        ALTER TABLE "EvaluationWorkspaceStates" ADD COLUMN "TenantId" character varying(128) NOT NULL DEFAULT 'default';
    END IF;
END $$;

-- EvaluationSessions（必需）
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'EvaluationSessions' AND column_name = 'TenantId'
    ) THEN
        ALTER TABLE "EvaluationSessions" ADD COLUMN "TenantId" character varying(128) NOT NULL DEFAULT 'default';
    END IF;
END $$;

-- EvaluationReports（可选）
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'EvaluationReports' AND column_name = 'TenantId'
    ) THEN
        ALTER TABLE "EvaluationReports" ADD COLUMN "TenantId" character varying(128);
    END IF;
END $$;

-- EvaluationAssets（可选）
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'EvaluationAssets' AND column_name = 'TenantId'
    ) THEN
        ALTER TABLE "EvaluationAssets" ADD COLUMN "TenantId" character varying(128);
    END IF;
END $$;

-- ============================================================
-- 阶段4：恢复主键约束
-- ============================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint 
        WHERE conname = 'PK_EvaluationWorkspaceStates' AND conrelid = '"EvaluationWorkspaceStates"'::regclass
    ) THEN
        ALTER TABLE "EvaluationWorkspaceStates" ADD CONSTRAINT "PK_EvaluationWorkspaceStates" PRIMARY KEY ("Id");
    END IF;
END $$;

-- ============================================================
-- 阶段5：创建 AppUsers 表（多租户用户表）
-- ============================================================

CREATE TABLE IF NOT EXISTS "AppUsers" (
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

-- ============================================================
-- 阶段6：创建新索引（多租户相关）
-- ============================================================

-- SandboxSessions 索引
CREATE UNIQUE INDEX IF NOT EXISTS "IX_SandboxSessions_TenantId_OwnerSubject_ScopeType_ScopeKey_Sa~" 
    ON "SandboxSessions" ("TenantId", "OwnerSubject", "ScopeType", "ScopeKey", "SandboxRole", "SessionKey");

-- SandboxAssets 索引
CREATE INDEX IF NOT EXISTS "IX_SandboxAssets_TenantId" ON "SandboxAssets" ("TenantId");

-- HiringRuntimeStates 索引
CREATE INDEX IF NOT EXISTS "IX_HiringRuntimeStates_TenantId_SessionId" 
    ON "HiringRuntimeStates" ("TenantId", "SessionId");
CREATE INDEX IF NOT EXISTS "IX_HiringRuntimeStates_TenantId_UpdatedAtUtc" 
    ON "HiringRuntimeStates" ("TenantId", "UpdatedAtUtc");

-- HiringAuditLogs 索引
CREATE INDEX IF NOT EXISTS "IX_HiringAuditLogs_TenantId_HireId_TimestampUtc" 
    ON "HiringAuditLogs" ("TenantId", "HireId", "TimestampUtc");
CREATE INDEX IF NOT EXISTS "IX_HiringAuditLogs_TenantId_SessionId_TimestampUtc" 
    ON "HiringAuditLogs" ("TenantId", "SessionId", "TimestampUtc");

-- HiringArtifactUploads 索引
CREATE INDEX IF NOT EXISTS "IX_HiringArtifactUploads_TenantId_SessionId_Kind_LogicalPath_C~" 
    ON "HiringArtifactUploads" ("TenantId", "SessionId", "Kind", "LogicalPath", "CompletedAtUtc");

-- HiringArtifactUploadParts 索引
CREATE INDEX IF NOT EXISTS "IX_HiringArtifactUploadParts_TenantId" ON "HiringArtifactUploadParts" ("TenantId");

-- HiringArtifacts 索引
CREATE INDEX IF NOT EXISTS "IX_HiringArtifacts_TenantId_SessionId_IsFinal" 
    ON "HiringArtifacts" ("TenantId", "SessionId", "IsFinal");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_HiringArtifacts_TenantId_SessionId_Kind_LogicalPath" 
    ON "HiringArtifacts" ("TenantId", "SessionId", "Kind", "LogicalPath");

-- EvaluationWorkspaceStates 索引
CREATE UNIQUE INDEX IF NOT EXISTS "IX_EvaluationWorkspaceStates_TenantId_OwnerSubject_EmployeeId" 
    ON "EvaluationWorkspaceStates" ("TenantId", "OwnerSubject", "EmployeeId");

-- EvaluationSessions 索引
CREATE INDEX IF NOT EXISTS "IX_EvaluationSessions_TenantId_EvaluatorHireId_TargetHireId" 
    ON "EvaluationSessions" ("TenantId", "EvaluatorHireId", "TargetHireId");
CREATE INDEX IF NOT EXISTS "IX_EvaluationSessions_TenantId_OwnerSubject_EmployeeId_Updated~" 
    ON "EvaluationSessions" ("TenantId", "OwnerSubject", "EmployeeId", "UpdatedAtUtc");

-- EvaluationReports 索引
CREATE INDEX IF NOT EXISTS "IX_EvaluationReports_TenantId" ON "EvaluationReports" ("TenantId");

-- EvaluationAssets 索引
CREATE INDEX IF NOT EXISTS "IX_EvaluationAssets_TenantId" ON "EvaluationAssets" ("TenantId");

-- AppUsers 索引（多租户用户表的关键索引）
CREATE INDEX IF NOT EXISTS "IX_AppUsers_ExternalUserId" ON "AppUsers" ("ExternalUserId");
CREATE INDEX IF NOT EXISTS "IX_AppUsers_TenantId" ON "AppUsers" ("TenantId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppUsers_TenantId_ExternalUserId" 
    ON "AppUsers" ("TenantId", "ExternalUserId");
CREATE INDEX IF NOT EXISTS "IX_AppUsers_TenantId_Username" ON "AppUsers" ("TenantId", "Username");

-- ============================================================
-- 阶段7：更新迁移历史记录
-- ============================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM "__EFMigrationsHistory" 
        WHERE "MigrationId" = '20260603165207_AddMultitenancySupport'
    ) THEN
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20260603165207_AddMultitenancySupport', '10.0.7');
    END IF;
END $$;

-- ======================================================================
-- 迁移完成
-- ======================================================================
-- 
-- 注意事项：
-- 1. 此脚本为 PostgreSQL 数据库设计
-- 2. ✅ 幂等性设计：使用 IF NOT EXISTS 检查，可安全重复执行
-- 3. 所有新增的 TenantId 字段默认值为 'default'
-- 4. AppUsers 表实现了多租户用户隔离，同一外部用户在不同租户下有独立记录
-- 5. 唯一约束 (TenantId, ExternalUserId) 确保租户内用户唯一性
-- 6. EvaluationWorkspaceStates 表新增了 GUID 主键
-- 
-- 执行说明：
-- - 如果是首次执行：将创建所有表、字段和索引
-- - 如果部分执行过：只会创建缺失的表、字段和索引
-- - 如果完全执行过：所有操作都会被安全跳过
-- 
-- 回滚说明：
-- 如需回滚，请执行迁移的 Down 方法或手动删除添加的字段和表
-- 
-- ======================================================================
