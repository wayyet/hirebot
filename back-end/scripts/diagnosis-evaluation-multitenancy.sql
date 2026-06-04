-- 评估服务多租户问题诊断 SQL

-- 1. 查看工作区状态表中的租户分布
SELECT 
    "TenantId",
    COUNT(*) as "工作区数量",
    COUNT(DISTINCT "OwnerSubject") as "不同用户数",
    COUNT(DISTINCT "EmployeeId") as "不同员工数",
    MIN("CreatedAtUtc") as "最早创建时间",
    MAX("UpdatedAtUtc") as "最近更新时间"
FROM "EvaluationWorkspaceStates"
GROUP BY "TenantId"
ORDER BY "工作区数量" DESC;

-- 2. 查看最近的工作区状态（检查租户ID是否正确）
SELECT 
    "OwnerSubject",
    "TenantId",
    "EmployeeId",
    "CreatedAtUtc",
    "UpdatedAtUtc",
    LENGTH("PayloadJson") as "PayloadSize"
FROM "EvaluationWorkspaceStates"
ORDER BY "UpdatedAtUtc" DESC
LIMIT 20;

-- 3. 查看评估沙箱实例的租户分布
SELECT 
    "TenantId",
    "SandboxRole",
    "State",
    COUNT(*) as "沙箱数量",
    COUNT(DISTINCT "OwnerSubject") as "不同用户数",
    MIN("CreatedAtUtc") as "最早创建时间",
    MAX("UpdatedAtUtc") as "最近更新时间"
FROM "SandboxInstances"
WHERE "ScopeType" = 'Managed'
  AND "SandboxRole" IN ('evaluation-target', 'evaluation-evaluator')
GROUP BY "TenantId", "SandboxRole", "State"
ORDER BY "TenantId", "SandboxRole", "沙箱数量" DESC;

-- 4. 检查是否有租户ID为 "tenant-default" 的旧数据（需要清理）
SELECT 
    'EvaluationWorkspaceStates' as "表名",
    COUNT(*) as "记录数"
FROM "EvaluationWorkspaceStates"
WHERE "TenantId" = 'tenant-default'
UNION ALL
SELECT 
    'SandboxInstances' as "表名",
    COUNT(*) as "记录数"
FROM "SandboxInstances"
WHERE "TenantId" = 'tenant-default'
  AND "ScopeType" = 'Managed'
  AND "SandboxRole" IN ('evaluation-target', 'evaluation-evaluator');

-- 5. 查找同一用户多次创建沙箱的情况（问题症状）
SELECT 
    "OwnerSubject",
    "TenantId",
    COUNT(*) as "沙箱数量",
    STRING_AGG(DISTINCT "SandboxId", ', ') as "沙箱ID列表",
    MIN("CreatedAtUtc") as "首次创建时间",
    MAX("CreatedAtUtc") as "最近创建时间",
    EXTRACT(EPOCH FROM (MAX("CreatedAtUtc") - MIN("CreatedAtUtc"))) / 3600 as "时间跨度(小时)"
FROM "SandboxInstances"
WHERE "ScopeType" = 'Managed'
  AND "SandboxRole" IN ('evaluation-target', 'evaluation-evaluator')
  AND "State" != 'Deleted'
GROUP BY "OwnerSubject", "TenantId"
HAVING COUNT(*) > 2
ORDER BY "沙箱数量" DESC;

-- 6. 查看工作区状态与沙箱实例的匹配情况
SELECT 
    ws."OwnerSubject",
    ws."TenantId" as "工作区租户ID",
    ws."EmployeeId",
    si_target."TenantId" as "Target沙箱租户ID",
    si_eval."TenantId" as "Evaluator沙箱租户ID",
    si_target."State" as "Target状态",
    si_eval."State" as "Evaluator状态",
    ws."UpdatedAtUtc" as "工作区更新时间"
FROM "EvaluationWorkspaceStates" ws
LEFT JOIN "SandboxInstances" si_target 
    ON si_target."SandboxRole" = 'evaluation-target'
    AND si_target."ScopeType" = 'Managed'
    AND si_target."State" != 'Deleted'
    AND ws."PayloadJson"::text LIKE '%' || si_target."SandboxId" || '%'
LEFT JOIN "SandboxInstances" si_eval 
    ON si_eval."SandboxRole" = 'evaluation-evaluator'
    AND si_eval."ScopeType" = 'Managed'
    AND si_eval."State" != 'Deleted'
    AND ws."PayloadJson"::text LIKE '%' || si_eval."SandboxId" || '%'
WHERE ws."UpdatedAtUtc" > NOW() - INTERVAL '7 days'
ORDER BY ws."UpdatedAtUtc" DESC
LIMIT 20;

-- 7. 统计每个租户的资源使用情况
SELECT 
    COALESCE(ws."TenantId", si."TenantId") as "TenantId",
    COUNT(DISTINCT ws."EmployeeId") as "评估员工数",
    COUNT(DISTINCT ws."OwnerSubject") as "不同用户数",
    COUNT(DISTINCT si."SandboxId") FILTER (WHERE si."State" = 'Running') as "运行中沙箱数",
    COUNT(DISTINCT si."SandboxId") FILTER (WHERE si."State" = 'Deleted') as "已删除沙箱数",
    COUNT(DISTINCT si."SandboxId") as "沙箱总数"
FROM "EvaluationWorkspaceStates" ws
FULL OUTER JOIN "SandboxInstances" si 
    ON si."ScopeType" = 'Managed'
    AND si."SandboxRole" IN ('evaluation-target', 'evaluation-evaluator')
GROUP BY COALESCE(ws."TenantId", si."TenantId")
ORDER BY "评估员工数" DESC;

-- 8. 检查最近1小时内的重复创建（实时监控）
SELECT 
    "OwnerSubject",
    "TenantId",
    "SandboxRole",
    COUNT(*) as "创建次数",
    STRING_AGG("SandboxId", ', ' ORDER BY "CreatedAtUtc") as "沙箱ID",
    STRING_AGG(TO_CHAR("CreatedAtUtc", 'HH24:MI:SS'), ', ' ORDER BY "CreatedAtUtc") as "创建时间"
FROM "SandboxInstances"
WHERE "ScopeType" = 'Managed'
  AND "SandboxRole" IN ('evaluation-target', 'evaluation-evaluator')
  AND "CreatedAtUtc" > NOW() - INTERVAL '1 hour'
GROUP BY "OwnerSubject", "TenantId", "SandboxRole"
HAVING COUNT(*) > 1
ORDER BY "创建次数" DESC;
