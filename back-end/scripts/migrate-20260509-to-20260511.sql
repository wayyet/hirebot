-- ============================================================
-- 增量迁移脚本
-- 覆盖范围：20260509073725 ~ 20260511055120
-- 适用于已执行过 schema.sql（含至 20260504093000）的数据库
-- ============================================================

-- -------------------------------------------------------
-- 20260509073725_AddSandboxInstanceTemplateId
-- 为 SandboxInstances 增加 TemplateId 字段及索引
-- -------------------------------------------------------
START TRANSACTION;

ALTER TABLE "SandboxInstances"
    ADD COLUMN IF NOT EXISTS "TemplateId" character varying(128);

CREATE INDEX IF NOT EXISTS "IX_SandboxInstances_OwnerSubject_TemplateId_SandboxRole_State"
    ON "SandboxInstances" ("OwnerSubject", "TemplateId", "SandboxRole", "State");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260509073725_AddSandboxInstanceTemplateId', '10.0.7')
ON CONFLICT DO NOTHING;

COMMIT;

-- -------------------------------------------------------
-- 20260510055253_AddActiveBranchId
-- 为 Instances 增加 active_branch_id 字段
-- -------------------------------------------------------
START TRANSACTION;

ALTER TABLE "Instances"
    ADD COLUMN IF NOT EXISTS active_branch_id character varying(120);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260510055253_AddActiveBranchId', '10.0.7')
ON CONFLICT DO NOTHING;

COMMIT;

-- -------------------------------------------------------
-- 20260511033548_RemoveViaQuickClone
-- 从 Instances 移除 via_quick_clone 字段
-- -------------------------------------------------------
START TRANSACTION;

ALTER TABLE "Instances"
    DROP COLUMN IF EXISTS via_quick_clone;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260511033548_RemoveViaQuickClone', '10.0.7')
ON CONFLICT DO NOTHING;

COMMIT;

-- -------------------------------------------------------
-- 20260511055120_AddSandboxIsInitialized
-- 为 SandboxInstances 增加 IsInitialized 字段
-- -------------------------------------------------------
START TRANSACTION;

ALTER TABLE "SandboxInstances"
    ADD COLUMN IF NOT EXISTS "IsInitialized" boolean NOT NULL DEFAULT false;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260511055120_AddSandboxIsInitialized', '10.0.7')
ON CONFLICT DO NOTHING;

COMMIT;
