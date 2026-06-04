-- ============================================================================
-- 雇佣流程轻量化表结构（替代 HiringRuntimeStates 的灾难性设计）
-- 生成时间: 2026-06-04
-- 用途: 独立存储阶段进度、结构化数据和外部配置
-- ============================================================================

-- 1. 雇佣阶段进度表（每个 hire 一行，轻量级状态）
CREATE TABLE "HiringStageProgresses" (
    "HireId" VARCHAR(64) PRIMARY KEY,
    "TenantId" VARCHAR(128),
    "CurrentStage" VARCHAR(40) NOT NULL,
    "PackagingTestCasesStatus" VARCHAR(40),
    "UpdatedAtUtc" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedBy" VARCHAR(256)
);

CREATE INDEX "IX_HiringStageProgresses_TenantId_UpdatedAtUtc" 
ON "HiringStageProgresses" ("TenantId", "UpdatedAtUtc");

COMMENT ON TABLE "HiringStageProgresses" IS '雇佣流程阶段进度（替代 HiringRuntimeStates 的 CurrentStage 字段）';
COMMENT ON COLUMN "HiringStageProgresses"."CurrentStage" IS '当前阶段: material | skill | external | ready_for_packaging';
COMMENT ON COLUMN "HiringStageProgresses"."PackagingTestCasesStatus" IS '测试用例状态: not_asked | generating | generated | null';


-- 2. 雇佣结构化数据表（键值对存储，替代 JSON blob）
CREATE TABLE "HiringStructuredData" (
    "Id" SERIAL PRIMARY KEY,
    "HireId" VARCHAR(64) NOT NULL,
    "TenantId" VARCHAR(128),
    "FieldKey" VARCHAR(256) NOT NULL,
    "FieldValue" TEXT,
    "CollectedAtUtc" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX "IX_HiringStructuredData_HireId" 
ON "HiringStructuredData" ("HireId");

CREATE INDEX "IX_HiringStructuredData_HireId_FieldKey" 
ON "HiringStructuredData" ("HireId", "FieldKey");

COMMENT ON TABLE "HiringStructuredData" IS '雇佣流程收集的结构化数据（替代 HiringRuntimeStates 的 WorkflowStateJson 中的 StructuredData）';
COMMENT ON COLUMN "HiringStructuredData"."FieldKey" IS '字段键，如: candidate.name, candidate.skills, job.title';
COMMENT ON COLUMN "HiringStructuredData"."FieldValue" IS '字段值（JSON 或纯文本）';


-- 3. 雇佣外部系统配置表（每个 hire 一行）
CREATE TABLE "HiringExternalConfigs" (
    "HireId" VARCHAR(64) PRIMARY KEY,
    "TenantId" VARCHAR(128),
    "ConfigJson" TEXT NOT NULL DEFAULT '{}',
    "UpdatedAtUtc" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedBy" VARCHAR(256)
);

CREATE INDEX "IX_HiringExternalConfigs_TenantId_UpdatedAtUtc" 
ON "HiringExternalConfigs" ("TenantId", "UpdatedAtUtc");

COMMENT ON TABLE "HiringExternalConfigs" IS '雇佣流程外部系统配置（飞书/钉钉/企业微信等，替代 HiringRuntimeStates 的部分 WorkflowStateJson）';
COMMENT ON COLUMN "HiringExternalConfigs"."ConfigJson" IS '外部系统配置 JSON（加密后的敏感信息）';


-- ============================================================================
-- 多租户支持（自动应用租户过滤器，由 DbContext 的 SaveChangesInterceptor 处理）
-- ============================================================================

-- 注意：TenantId 字段由应用层自动填充，无需触发器


-- ============================================================================
-- 数据迁移说明
-- ============================================================================
-- 1. 如需从旧的 HiringRuntimeStates 迁移数据，参考以下逻辑：
--    - CurrentStage, CollectionPhase → HiringStageProgresses.CurrentStage
--    - WorkflowStateJson.StructuredData (JSON对象) → HiringStructuredData (拆分为多行)
--    - WorkflowStateJson.ExternalSystemConfig → HiringExternalConfigs.ConfigJson
--
-- 2. 以下数据不再持久化（改为运行时处理）：
--    - PackagesJson (5-20MB 模板包) → 文件系统按需加载 + IMemoryCache
--    - ConversationCacheJson → 前端使用 localStorage + 沙箱历史
--    - WorkflowStateJson.Materials → 已有 HiringMaterialFiles 表
--    - WorkflowStateJson.StageCompletion → 从 StructuredData 实时计算
--    - WorkflowStateJson.HandoffItems → 从沙箱 artifact 实时解析
--    - WorkflowStateJson.LatestDispatches → 不需要持久化
--
-- ============================================================================
