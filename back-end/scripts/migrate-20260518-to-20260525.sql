-- 迁移脚本：2026-05-18 → 2026-05-25
-- 对应 EF Migration: AddRuntimeSandboxScopeType
--
-- 将存量个人分身运行时沙箱的 scope_type 从 'hire' 修正为 'runtime'，
-- 区分雇佣流程沙箱与个人运行时沙箱，避免跨类别操作相互影响。
-- scope_key 格式: instance:{instanceId}

-- "SandboxInstances" 表（EF Core 大驼峰表名，PostgreSQL 需加引号）
UPDATE "SandboxInstances"
SET "ScopeType" = 'runtime'
WHERE "ScopeType" = 'hire'
  AND "ScopeKey" LIKE 'instance:%';

-- "SandboxSessions" 表（如存在相同特征的会话记录）
UPDATE "SandboxSessions"
SET "ScopeType" = 'runtime'
WHERE "ScopeType" = 'hire'
  AND "ScopeKey" LIKE 'instance:%';
