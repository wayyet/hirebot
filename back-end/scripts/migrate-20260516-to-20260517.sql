-- ============================================================
-- 增量迁移脚本
-- 覆盖范围：20260516153629 ~ 20260517000000
-- 适用于已执行过 migrate-20260515-to-20260516.sql 的数据库
-- ============================================================

-- -------------------------------------------------------
-- 20260517000000_SplitHiringRuntimePayload
-- 将 HiringRuntimeStates.PayloadJson 单体字段拆分为三列：
--   - PayloadJson      → 身份元数据（TemplateId、OwnerSubject 等标量）
--   - PackagesJson     → 模板包定义（RoleTemplatePackage、WorkingTemplatePackage、DiscoverySkill）
--   - WorkflowStateJson → 动态工作流数据（StructuredData、Materials、HandoffItems 等）
-- -------------------------------------------------------
START TRANSACTION;

-- 1. 添加新列（默认 '{}' 保证非 NULL 约束）
ALTER TABLE "HiringRuntimeStates"
    ADD COLUMN IF NOT EXISTS "PackagesJson" text NOT NULL DEFAULT '{}';

ALTER TABLE "HiringRuntimeStates"
    ADD COLUMN IF NOT EXISTS "WorkflowStateJson" text NOT NULL DEFAULT '{}';

-- 2. 从旧 PayloadJson 中提取模板包定义 → PackagesJson
UPDATE "HiringRuntimeStates"
SET "PackagesJson" = jsonb_build_object(
    'roleTemplatePackage', ("PayloadJson"::jsonb) -> 'roleTemplatePackage',
    'workingTemplatePackage', ("PayloadJson"::jsonb) -> 'workingTemplatePackage',
    'discoverySkill', ("PayloadJson"::jsonb) -> 'discoverySkill'
)::text
WHERE "PackagesJson" = '{}';

-- 3. 从旧 PayloadJson 中提取动态工作流状态 → WorkflowStateJson
UPDATE "HiringRuntimeStates"
SET "WorkflowStateJson" = jsonb_build_object(
    'structuredData', COALESCE(("PayloadJson"::jsonb) -> 'structuredData', '{}'::jsonb),
    'materials', COALESCE(("PayloadJson"::jsonb) -> 'materials', '[]'::jsonb),
    'stageCompletion', COALESCE(("PayloadJson"::jsonb) -> 'stageCompletion', '[]'::jsonb),
    'handoffItems', COALESCE(("PayloadJson"::jsonb) -> 'handoffItems', '[]'::jsonb),
    'latestDispatches', COALESCE(("PayloadJson"::jsonb) -> 'latestDispatches', '[]'::jsonb),
    'configGovernance', ("PayloadJson"::jsonb) -> 'configGovernance',
    'stageReadiness', COALESCE(("PayloadJson"::jsonb) -> 'stageReadiness', '[]'::jsonb)
)::text
WHERE "WorkflowStateJson" = '{}';

-- 4. 将 PayloadJson 裁剪为仅保留身份元数据（去除体积大的包/状态字段）
UPDATE "HiringRuntimeStates"
SET "PayloadJson" = jsonb_build_object(
    'templateId', ("PayloadJson"::jsonb) -> 'templateId',
    'templateName', ("PayloadJson"::jsonb) -> 'templateName',
    'ownerSubject', ("PayloadJson"::jsonb) -> 'ownerSubject',
    'tenantId', ("PayloadJson"::jsonb) -> 'tenantId',
    'operatorId', ("PayloadJson"::jsonb) -> 'operatorId',
    'sandboxId', ("PayloadJson"::jsonb) -> 'sandboxId',
    'employeeId', ("PayloadJson"::jsonb) -> 'employeeId',
    'isConversationPaused', ("PayloadJson"::jsonb) -> 'isConversationPaused',
    'isConversationResponding', ("PayloadJson"::jsonb) -> 'isConversationResponding',
    'isTemplateUploadPending', ("PayloadJson"::jsonb) -> 'isTemplateUploadPending',
    'templateUploadRetryCount', ("PayloadJson"::jsonb) -> 'templateUploadRetryCount',
    'templateUploadLastError', ("PayloadJson"::jsonb) -> 'templateUploadLastError',
    'templateUploadLastAttemptAt', ("PayloadJson"::jsonb) -> 'templateUploadLastAttemptAt'
)::text;

-- 5. 记录 EF 迁移历史
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260517000000_SplitHiringRuntimePayload', '10.0.7')
ON CONFLICT DO NOTHING;

COMMIT;
