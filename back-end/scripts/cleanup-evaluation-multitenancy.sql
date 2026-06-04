-- 评估服务多租户数据清理脚本
-- ⚠️ 警告：执行前请先备份数据库！

-- 说明：
-- 此脚本用于清理多租户改造前创建的数据，这些数据的 TenantId 为硬编码的 "tenant-default"
-- 如果这些数据属于真实的租户，需要手动更新为正确的租户ID

-- ========================================
-- 步骤 1: 备份数据（强烈建议）
-- ========================================

-- 创建备份表（可选）
CREATE TABLE IF NOT EXISTS "EvaluationWorkspaceStates_Backup_20260604" AS
SELECT * FROM "EvaluationWorkspaceStates"
WHERE "TenantId" = 'tenant-default';

CREATE TABLE IF NOT EXISTS "SandboxInstances_Backup_20260604" AS
SELECT * FROM "SandboxInstances"
WHERE "TenantId" = 'tenant-default'
  AND "ScopeType" = 'Managed'
  AND "SandboxRole" IN ('evaluation-target', 'evaluation-evaluator');

-- ========================================
-- 步骤 2: 检查要清理的数据
-- ========================================

-- 查看工作区状态中待清理的数据
SELECT 
    "OwnerSubject",
    "EmployeeId",
    "CreatedAtUtc",
    "UpdatedAtUtc"
FROM "EvaluationWorkspaceStates"
WHERE "TenantId" = 'tenant-default'
ORDER BY "UpdatedAtUtc" DESC;

-- 查看沙箱实例中待清理的数据
SELECT 
    "SandboxId",
    "SandboxRole",
    "OwnerSubject",
    "State",
    "CreatedAtUtc"
FROM "SandboxInstances"
WHERE "TenantId" = 'tenant-default'
  AND "ScopeType" = 'Managed'
  AND "SandboxRole" IN ('evaluation-target', 'evaluation-evaluator')
ORDER BY "CreatedAtUtc" DESC;

-- ========================================
-- 步骤 3: 清理方案选择
-- ========================================

-- 方案 A: 删除所有 tenant-default 的评估相关数据（推荐，如果这些是无效数据）
-- ⚠️ 执行前请确认这些数据确实不需要！

/*
BEGIN;

-- 删除工作区状态
DELETE FROM "EvaluationWorkspaceStates"
WHERE "TenantId" = 'tenant-default';

-- 删除沙箱实例（仅删除评估相关的）
DELETE FROM "SandboxInstances"
WHERE "TenantId" = 'tenant-default'
  AND "ScopeType" = 'Managed'
  AND "SandboxRole" IN ('evaluation-target', 'evaluation-evaluator');

-- 检查删除结果
SELECT 'EvaluationWorkspaceStates' as 表名, COUNT(*) as 剩余记录数
FROM "EvaluationWorkspaceStates"
WHERE "TenantId" = 'tenant-default'
UNION ALL
SELECT 'SandboxInstances' as 表名, COUNT(*) as 剩余记录数
FROM "SandboxInstances"
WHERE "TenantId" = 'tenant-default'
  AND "ScopeType" = 'Managed'
  AND "SandboxRole" IN ('evaluation-target', 'evaluation-evaluator');

-- 如果确认无误，提交事务
COMMIT;
-- 如果需要回滚，执行： ROLLBACK;
*/

-- ========================================
-- 方案 B: 更新为正确的租户ID（如果数据有效，需要手动映射）
-- ========================================

-- 示例：将某个用户的数据更新为正确的租户ID
/*
BEGIN;

-- 假设 OwnerSubject 格式为 "tenant-abc:user-123"
-- 可以从中提取出租户ID

-- 更新工作区状态的租户ID
UPDATE "EvaluationWorkspaceStates"
SET "TenantId" = SPLIT_PART("OwnerSubject", ':', 1)
WHERE "TenantId" = 'tenant-default'
  AND "OwnerSubject" LIKE '%:%'
  AND SPLIT_PART("OwnerSubject", ':', 1) != '';

-- 更新沙箱实例的租户ID
UPDATE "SandboxInstances"
SET "TenantId" = SPLIT_PART("OwnerSubject", ':', 1),
    "OperatorId" = SPLIT_PART("OwnerSubject", ':', 2)
WHERE "TenantId" = 'tenant-default'
  AND "ScopeType" = 'Managed'
  AND "SandboxRole" IN ('evaluation-target', 'evaluation-evaluator')
  AND "OwnerSubject" LIKE '%:%'
  AND SPLIT_PART("OwnerSubject", ':', 1) != '';

-- 检查更新结果
SELECT 
    "TenantId",
    COUNT(*) as 记录数
FROM "EvaluationWorkspaceStates"
GROUP BY "TenantId"
ORDER BY 记录数 DESC;

-- 如果确认无误，提交事务
COMMIT;
-- 如果需要回滚，执行： ROLLBACK;
*/

-- ========================================
-- 步骤 4: 清理孤儿数据（可选）
-- ========================================

-- 查找没有对应工作区状态的沙箱实例
SELECT 
    si."SandboxId",
    si."SandboxRole",
    si."OwnerSubject",
    si."TenantId",
    si."State",
    si."CreatedAtUtc"
FROM "SandboxInstances" si
WHERE si."ScopeType" = 'Managed'
  AND si."SandboxRole" IN ('evaluation-target', 'evaluation-evaluator')
  AND NOT EXISTS (
      SELECT 1 
      FROM "EvaluationWorkspaceStates" ws
      WHERE ws."OwnerSubject" = si."OwnerSubject"
        AND ws."TenantId" = si."TenantId"
  )
ORDER BY si."CreatedAtUtc" DESC;

-- 删除孤儿沙箱实例（已删除状态的）
/*
DELETE FROM "SandboxInstances"
WHERE "ScopeType" = 'Managed'
  AND "SandboxRole" IN ('evaluation-target', 'evaluation-evaluator')
  AND "State" = 'Deleted'
  AND NOT EXISTS (
      SELECT 1 
      FROM "EvaluationWorkspaceStates" ws
      WHERE ws."OwnerSubject" = "SandboxInstances"."OwnerSubject"
        AND ws."TenantId" = "SandboxInstances"."TenantId"
  );
*/

-- ========================================
-- 步骤 5: 验证清理结果
-- ========================================

-- 检查是否还有 tenant-default 的数据
SELECT 
    'EvaluationWorkspaceStates' as 表名,
    COUNT(*) as tenant_default_记录数,
    COUNT(DISTINCT "OwnerSubject") as 不同用户数
FROM "EvaluationWorkspaceStates"
WHERE "TenantId" = 'tenant-default'
UNION ALL
SELECT 
    'SandboxInstances' as 表名,
    COUNT(*) as tenant_default_记录数,
    COUNT(DISTINCT "OwnerSubject") as 不同用户数
FROM "SandboxInstances"
WHERE "TenantId" = 'tenant-default'
  AND "ScopeType" = 'Managed'
  AND "SandboxRole" IN ('evaluation-target', 'evaluation-evaluator');

-- 查看当前租户分布
SELECT 
    "TenantId",
    COUNT(*) as 工作区数量,
    COUNT(DISTINCT "OwnerSubject") as 不同用户数
FROM "EvaluationWorkspaceStates"
GROUP BY "TenantId"
ORDER BY 工作区数量 DESC;

-- ========================================
-- 步骤 6: 删除备份表（清理完成后）
-- ========================================

-- ⚠️ 只有在确认清理成功后才执行！
/*
DROP TABLE IF EXISTS "EvaluationWorkspaceStates_Backup_20260604";
DROP TABLE IF EXISTS "SandboxInstances_Backup_20260604";
*/
