-- 招聘流程测试数据
-- 执行前请确保 HireBot 数据库已创建并可连接

-- 1. 插入 HiringSessionEntity (招聘会话)
INSERT INTO "HiringSessions" (
    "SessionId",
    "HireId",
    "TemplateId",
    "PackageId",
    "PackageVersion",
    "PackageHash",
    "SourceZipSha256",
    "SourceZipStoragePath",
    "SourceZipSizeBytes",
    "OwnerSubject",
    "TenantId",
    "OperatorId",
    "CreatedAtUtc",
    "DeletedAtUtc",
    "DeletedBy"
) VALUES (
    'session_test_001',
    'hire_test_001',
    'template_employee_coach',
    'pkg_test_001',
    '1.0.0',
    'abc123hash',
    'def456sha',
    '/packages/test.zip',
    1024000,
    'user:test-owner',
    'tenant_test',
    'operator_test',
    NOW(),
    NULL,
    NULL
) ON CONFLICT DO NOTHING;

-- 2. 插入 HiringRuntimeStateEntity (招聘运行时状态)
INSERT INTO "HiringRuntimeStates" (
    "HireId",
    "SessionId",
    "CurrentStage",
    "CollectionPhase",
    "PayloadJson",
    "CreatedAtUtc",
    "UpdatedAtUtc"
) VALUES (
    'hire_test_001',
    'session_test_001',
    'onboarding',
    'collecting',
    '{"employee_name":"张三","department":"技术部","position":"高级工程师"}',
    NOW(),
    NOW()
) ON CONFLICT DO NOTHING;

-- 3. 插入第二个招聘会话
INSERT INTO "HiringSessions" (
    "SessionId",
    "HireId",
    "TemplateId",
    "PackageId",
    "PackageVersion",
    "OwnerSubject",
    "TenantId",
    "OperatorId",
    "CreatedAtUtc"
) VALUES (
    'session_test_002',
    'hire_test_002',
    'template_asset_guardian',
    'pkg_test_002',
    '1.0.0',
    'user:test-owner',
    'tenant_test',
    'operator_test',
    NOW()
) ON CONFLICT DO NOTHING;

INSERT INTO "HiringRuntimeStates" (
    "HireId",
    "SessionId",
    "CurrentStage",
    "CollectionPhase",
    "PayloadJson",
    "CreatedAtUtc",
    "UpdatedAtUtc"
) VALUES (
    'hire_test_002',
    'session_test_002',
    'evaluation',
    'collecting',
    '{"employee_name":"李四","department":"财务部","position":"财务经理"}',
    NOW(),
    NOW()
) ON CONFLICT DO NOTHING;

-- 验证插入
SELECT 'HiringSessions count: ' || COUNT(*) FROM "HiringSessions" WHERE "HireId" LIKE 'hire_test_%';
SELECT 'HiringRuntimeStates count: ' || COUNT(*) FROM "HiringRuntimeStates" WHERE "HireId" LIKE 'hire_test_%';
