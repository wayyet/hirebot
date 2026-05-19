-- ============================================================
-- 增量迁移脚本
-- 覆盖范围：20260517000000 ~ 20260518083910
-- 适用于已执行过 migrate-20260516-to-20260517.sql 的数据库
-- ============================================================

-- -------------------------------------------------------
-- 20260518083910_AddHiringMaterialFiles
-- 新增 hiring_material_files 表，用于记录雇佣流程中上传的材料文件。
-- 包含文件路径、格式、SHA256 校验、租户/操作员信息及软删除时间戳。
-- -------------------------------------------------------
START TRANSACTION;

CREATE TABLE IF NOT EXISTS hiring_material_files (
    material_file_id         uuid                     NOT NULL,
    hire_id                  character varying(64)    NOT NULL,
    session_id               character varying(64)    NOT NULL,
    relative_path            character varying(1024)  NOT NULL,
    original_file_name       character varying(512)   NOT NULL,
    storage_path             character varying(1024)  NOT NULL,
    format                   character varying(32)    NOT NULL,
    mime_type                character varying(120)   NULL,
    size_bytes               bigint                   NOT NULL,
    sha256                   character varying(64)    NOT NULL,
    requested_category_title character varying(160)   NULL,
    tenant_id                character varying(128)   NOT NULL,
    operator_id              character varying(128)   NOT NULL,
    uploaded_by              character varying(256)   NOT NULL,
    uploaded_at_utc          timestamp with time zone NOT NULL,
    updated_at_utc           timestamp with time zone NOT NULL,
    deleted_at_utc           timestamp with time zone NULL,
    CONSTRAINT "PK_hiring_material_files" PRIMARY KEY (material_file_id)
);

-- 按 hire_id + session_id + 上传时间查询的复合索引
CREATE INDEX IF NOT EXISTS "IX_hiring_material_files_hire_id_session_id_uploaded_at_utc"
    ON hiring_material_files (hire_id, session_id, uploaded_at_utc);

-- 同一 session 内 relative_path 唯一，防止重复上传覆盖
CREATE UNIQUE INDEX IF NOT EXISTS "IX_hiring_material_files_session_id_relative_path"
    ON hiring_material_files (session_id, relative_path);

-- SHA256 索引，用于快速去重检测
CREATE INDEX IF NOT EXISTS "IX_hiring_material_files_sha256"
    ON hiring_material_files (sha256);

COMMIT;
