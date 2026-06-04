-- HireBot 阶段进度表扩展 - 上传文件列表 + 产物包结构
-- 日期：2026-06-04
-- 用途：在 HiringStageProgresses 表中增加两个持久化字段：
--   1. UploadedFilesJson  - 对话上传文件列表（仅元数据，不含文件内容）
--   2. PackageStructureJson - 最新产物包结构（ZIP 文件名 + 包内文件路径列表）
-- 这两个字段使雇佣页面可以在刷新/重新进入后恢复文件列表状态和在 TODO 面板持续显示最新包。

ALTER TABLE "HiringStageProgresses"
ADD COLUMN IF NOT EXISTS "UploadedFilesJson" jsonb,
ADD COLUMN IF NOT EXISTS "PackageStructureJson" jsonb;

COMMENT ON COLUMN "HiringStageProgresses"."UploadedFilesJson" IS '对话上传文件列表 JSON（仅元数据，不含文件内容）';
COMMENT ON COLUMN "HiringStageProgresses"."PackageStructureJson" IS '最新产物包结构 JSON（{ fileName, fileNames[] }）';
