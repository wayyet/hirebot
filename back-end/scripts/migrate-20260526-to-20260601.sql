-- 迁移脚本：2026-05-26 (RemoveHiringCredentialBindings) → 2026-06-01 (AddWorkspaceRelativePathToHiringMaterialFiles)
-- 对应 EF Migration: AddWorkspaceRelativePathToHiringMaterialFiles
--
-- 为 "hiring_material_files" 表新增 "workspace_relative_path" 列，
-- 用于记录素材文件在工作区中的相对路径（如 docs/resume.pdf），
-- 便于前端或运行时按相对路径定位文件，无需依赖绝对路径或外部存储 URL。
-- 存量记录默认为 NULL，新建素材文件起开始填充。

ALTER TABLE "hiring_material_files"
    ADD COLUMN IF NOT EXISTS "workspace_relative_path" character varying(1024);
