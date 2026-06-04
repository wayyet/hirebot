-- ============================================================
-- HireBot 阶段进度表扩展迁移脚本
-- 创建时间：2026-06-04
-- 用途：扩展 HiringStageProgresses 表，增加运行时状态字段
--      （阶段覆盖配置 + 下游运行记录）
-- ============================================================

-- 扩展现有的阶段进度表
ALTER TABLE "HiringStageProgresses"
ADD COLUMN IF NOT EXISTS "StageOverridesJson" jsonb,
ADD COLUMN IF NOT EXISTS "DownstreamRunsJson" jsonb;

-- 添加注释
COMMENT ON COLUMN "HiringStageProgresses"."StageOverridesJson" IS '阶段覆盖配置 JSON（用户手动修改的阶段配置）';
COMMENT ON COLUMN "HiringStageProgresses"."DownstreamRunsJson" IS '下游运行记录 JSON（外部系统调用记录）';

-- 示例数据格式
-- StageOverridesJson: {"goal_collection": {"skipValidation": true}, "skill_design": {...}}
-- DownstreamRunsJson: {"runId1": {"status": "success", "result": {...}}, "runId2": {...}}
