-- 添加 Description 字段到 Instances 表，用于存储模板简短描述（列表展示用）

ALTER TABLE "Instances"
    ADD COLUMN IF NOT EXISTS description TEXT;

COMMENT ON COLUMN "Instances".description IS '模板简短描述文本，用于列表展示';
