# Asset Lifecycle Slice

## Goal
- Maintain full visibility from procurement to retirement.
- Minimize account-vs-physical mismatch.
- Improve utilization while keeping compliance intact.

## Core entities
- asset
- asset_tag (QR/RFID)
- custody_record
- maintenance_work_order
- depreciation_schedule
- inventory_task
- discrepancy_ticket
- retirement_request

## Key actions
- register_asset
- assign_or_borrow_asset
- return_asset
- create_maintenance_plan
- run_inventory_check
- reconcile_discrepancy
- issue_retirement_recommendation

## Constraints
- custody updates must be approved for controlled asset classes
- disposal suggestions require lifecycle evidence and risk notes
