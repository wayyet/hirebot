-- 迁移脚本：2026-05-25 (AddRuntimeSandboxScopeType) → 2026-05-25 (AddSandboxMetadata)
-- 对应 EF Migration: AddSandboxMetadata
--
-- 为 "SandboxInstances" 表新增 "Metadata" JSONB 列，
-- 用于存储沙箱业务语义元数据（如 user_subject、hire_id、instance_id 等），
-- 通过列表接口即可直接识别沙箱归属与业务上下文，无需关联其他表。
-- 存量记录默认为 NULL，新建沙箱起开始填充。

ALTER TABLE "SandboxInstances"
    ADD COLUMN IF NOT EXISTS "Metadata" jsonb;
