-- 迁移脚本：2026-06-01 → 2026-06-03 (AddAppUsersTable)
-- 添加 app_users 表用于存储从 JWT claims 同步的用户信息，
-- 支持在业务实体中关联并展示创建人、更新人等用户详细信息。
--
-- 此表与现有的 "Users" 表独立，"Users" 表用于基于密码的本地认证，
-- "AppUsers" 表用于 OIDC/JWT 认证用户的元数据缓存。

CREATE TABLE IF NOT EXISTS "AppUsers" (
    "Id" character varying(256) NOT NULL,
    "TenantId" character varying(128) NOT NULL,
    "Username" character varying(128) NOT NULL,
    "DisplayName" character varying(256) NOT NULL,
    "FamilyName" character varying(256),
    "GivenName" character varying(256),
    "Email" character varying(256) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "LastSeenAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AppUsers" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_AppUsers_TenantId" ON "AppUsers" ("TenantId");
CREATE INDEX IF NOT EXISTS "IX_AppUsers_TenantId_Username" ON "AppUsers" ("TenantId", "Username");
