-- 迁移脚本：2026-05-25 (AddSandboxMetadata) → 2026-05-26 (RemoveHiringCredentialBindings)
-- 对应 EF Migration: RemoveHiringCredentialBindings
--
-- 删除 "HiringCredentialBindings" 表。
-- 该表原用于存储雇佣流程中的凭证绑定关系，现已废弃，
-- 相关凭证管理已迁移至其他机制，存量数据无需保留。

DROP TABLE IF EXISTS "HiringCredentialBindings";
