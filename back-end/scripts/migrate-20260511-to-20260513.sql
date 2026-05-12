-- ============================================================
-- 增量迁移脚本
-- 覆盖范围：20260511055120 ~ 20260513000000
-- 适用于已执行过 migrate-20260509-to-20260511.sql 的数据库
-- ============================================================

-- -------------------------------------------------------
-- 20260513000000_AddHiringConversationCache
-- 为 HiringRuntimeStates 增加 ConversationCacheJson 字段
-- 用于持久化招聘页面的对话历史（含 artifact/stage_gate 消息
-- 及阶段状态覆盖），支持刷新页面后恢复完整对话上下文
-- -------------------------------------------------------
START TRANSACTION;

ALTER TABLE "HiringRuntimeStates"
    ADD COLUMN IF NOT EXISTS "ConversationCacheJson" text NOT NULL DEFAULT '{}';

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260513000000_AddHiringConversationCache', '10.0.7')
ON CONFLICT DO NOTHING;

COMMIT;
