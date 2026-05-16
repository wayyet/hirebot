-- ============================================================
-- 增量迁移脚本
-- 覆盖范围：20260513000000 ~ 20260515102944
-- 适用于已执行过 migrate-20260511-to-20260513.sql 的数据库
-- ============================================================

-- -------------------------------------------------------
-- 20260515102944_AddInstanceDescribeDocument
-- 为 Instances 增加 describe_document 字段
-- 用于存储实例描述文档内容
-- -------------------------------------------------------
START TRANSACTION;

ALTER TABLE "Instances"
    ADD COLUMN IF NOT EXISTS "describe_document" text;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260515102944_AddInstanceDescribeDocument', '10.0.7')
ON CONFLICT DO NOTHING;

COMMIT;
