-- ============================================================
-- 增量迁移脚本
-- 覆盖范围：20260515102944 ~ 20260516153629
-- 适用于已执行过 migrate-20260513-to-20260515.sql 的数据库
-- ============================================================

-- -------------------------------------------------------
-- 20260516153629_AddEvaluationWorkspaceStatePersistence
-- 新增 EvaluationWorkspaceStates 表
-- 用于持久化评估 workspace 状态，移除 API 进程内缓存依赖
-- -------------------------------------------------------
START TRANSACTION;

CREATE TABLE IF NOT EXISTS "EvaluationWorkspaceStates"
(
    "OwnerSubject" character varying(120) NOT NULL,
    "EmployeeId" character varying(120) NOT NULL,
    "PayloadJson" text NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_EvaluationWorkspaceStates" PRIMARY KEY ("OwnerSubject", "EmployeeId")
);

CREATE INDEX IF NOT EXISTS "IX_EvaluationWorkspaceStates_UpdatedAtUtc"
    ON "EvaluationWorkspaceStates" ("UpdatedAtUtc");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260516153629_AddEvaluationWorkspaceStatePersistence', '10.0.7')
ON CONFLICT DO NOTHING;

COMMIT;
