-- ============================================================
-- 数据修复脚本
-- 日期：2026-06-04
-- 功能：修复 HiringRuntimeStates 中缺失的 OwnerSubject
-- 问题：多租户改造后，旧数据的 PayloadJson 中 ownerSubject 可能为 null
-- 解决：使用 TenantId:OperatorId 作为 fallback
-- ============================================================

DO $$
DECLARE
    updated_count INT := 0;
BEGIN
    -- 更新所有 OwnerSubject 为 null 或空字符串的记录
    UPDATE "HiringRuntimeStates"
    SET "PayloadJson" = jsonb_set(
        "PayloadJson"::jsonb,
        '{ownerSubject}',
        to_jsonb(CONCAT(
            COALESCE("TenantId", 'tenant-default'),
            ':',
            COALESCE(
                (("PayloadJson"::jsonb) ->> 'operatorId'),
                'operator-default'
            )
        ))
    )::text
    WHERE 
        ("PayloadJson"::jsonb) ->> 'ownerSubject' IS NULL
        OR ("PayloadJson"::jsonb) ->> 'ownerSubject' = '';

    GET DIAGNOSTICS updated_count = ROW_COUNT;
    
    RAISE NOTICE '修复完成：更新了 % 条记录', updated_count;
END $$;
