# Instance Fixtures

This directory intentionally keeps a single fixture package for local evaluation:

- Template: Asset Guardian — 办公资产全生命周期管理 NCrew
- TemplateId: 019dcfca-08a3-7a2a-bd14-09e790eab6f7
- Hire fixture folder: hire_dev_seed_401_asset-guardian

Purpose:
- deterministic local fixture-hire binding
- deterministic dual-sandbox evaluation testcase source

Catalog note:
- Build service / template pool may expose a **different** `templateId` (UUID) for the same logical template. Add aliases in `template-bindings.json` (`templateId` → `fixtureEmployeeId`) so `POST .../fixture-hire` keeps working after catalog updates.

Fixture-hire note:
- `fixture-hire` only **succeeds** for instances in `interning_ai` with eval phase `pending_materials` / `pending_skill_upload` (see `IsUploadSkillReadyInstance`). Set `"status": "interning_ai"` in a package’s `instance.json` when that fixture should open the evaluation flow from the template pool. After changing fixture files, call `POST /api/v1/migrations/fixture-instances` again to refresh the in-memory store and `Instances` snapshots.
