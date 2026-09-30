# Feature Parity Matrix — Batch 1 Baseline

This matrix is based on the current Laravel routes, tenant migrations, controllers, services, and project documentation. It will be refined as each module is converted.

| Laravel module | Current tenant tables / central source | Key audited rules | Planned C# surface | Ownership | Sync | Entitlement / permission | Status |
|---|---|---|---|---|---|---|---|
| Tenant/subscription/license | central tenants, businesses, plans, subscriptions, licenses, license_activations | server authority; Windows device limit; signed lease | Activation + subscription status | Cloud | Check-in | signed plan/license | Audited |
| Pharmacy auth | users, roles, permissions, role_user, permission_role | tenant resolved before user; RBAC enforced server-side | Login + session | Hybrid | Yes | role permissions | Audited |
| Dashboard/alerts | derived | tenant operational state | Dashboard | Local-first | Delta | dashboard.view | Audited |
| Medicine master | medicine_categories, manufacturers, medicines | tenant-owned master data | Medicines/categories/manufacturers | Hybrid | Yes | medicines.manage | Audited |
| Suppliers | suppliers | tenant-owned | Suppliers | Hybrid | Yes | purchases.manage | Audited |
| Purchase orders | purchase_orders, purchase_order_lines | draft→submitted→approved; decimal math | Purchasing | Local-first | Yes | purchases.manage / approve | Audited |
| Goods receipts | goods_receipts, goods_receipt_lines | receipt document before stock posting; idempotent posting | Goods receipt | Local-first | Yes | purchases.manage | Audited |
| Purchase invoices/payments | purchase_invoices, supplier_payments | no overpayment; source immutable; idempotency-aware | Payables | Local-first | Yes | purchases.pay | Audited |
| Purchase returns | not identified | required by master prompt but absent from current audited tenant schema/routes | Purchase returns | TBD | Yes | TBD | Parity gap |
| Branch/location inventory | branches, stock_locations | tenant-owned branch/location model | Locations | Hybrid | Yes | inventory.manage | Audited |
| Batch inventory | product_batches, stock_movements, batch_status_events | immutable movement ledger; FEFO; expired/quarantined/recalled/damaged excluded | Inventory/batches/expiry | Local-first | Yes | inventory.manage/status | Audited |
| Inventory adjustments | inventory_adjustments, inventory_adjustment_lines | source-backed adjustment; no negative stock | Stock adjustment | Local-first | Yes | inventory.adjust | Audited |
| Customers | customers | tenant-owned | Customers | Hybrid | Yes | customers.manage | Audited |
| POS sales | sales, sale_lines, sale_batch_allocations, sale_payments | atomic sale+stock+accounting; FEFO; decimal money | POS + invoices/receipts | Local-first | Yes | pos.sell | Audited |
| Sales returns | sale_returns, sale_return_lines, sale_return_allocations, sale_return_refunds | reversal/audit path; do not mutate old sale silently | Sales returns | Local-first | Yes | returns.manage | Audited |
| Prescriptions | fields on sales | integrated with sales workflow | POS prescription fields | Local-first | Yes | pos.sell | Audited |
| Cashier shifts | cashier_shifts | open/close around business-day workflow | Shift open/close | Local-first | Yes | daily_closing.perform | Audited |
| Daily Closing | daily_closings, daily_closing_events | one final close per branch/date; system totals; reopen audited | Daily Closing | Local-first | Yes | perform/approve/reopen | Audited |
| Cash safe | cash_safes, safe_movements, safe_closings, safe_closing_events | controlled movement/finalize/approve/reopen | Safe | Local-first | Yes | safe.* | Audited |
| Accounting | ledger_accounts, journal_entries, journal_lines | balanced double-entry; source linked; reversals instead of edits | Accounting | Local-first | Yes | accounting.manage | Audited |
| Expenses | expenses | journal-backed; reversal preserves original | Expenses | Local-first | Yes | accounting.manage | Audited |
| Accounting adjustments | accounting_adjustments | documented debit/credit + reversal | Adjustments | Local-first | Yes | accounting.manage | Audited |
| Reports | derived | reconcile inventory/accounting/closing | Reports/export/print | Local | No/derived | reports.view | Audited |
| Pharmacy settings | pharmacy_settings | locale/timezone/business-day/inventory policies | Settings | Hybrid | Yes | settings.manage | Audited |
| Localization | settings + lang resources | English/Dari/Pashto, RTL | Whole UI | Local config | Optional | none | Audited |
| Backup/restore | existing Laravel backup services/commands | safe backup/verify/restore | Native local backup | Local | Optional cloud later | owner/admin policy | Implemented (Batch 19) |
| Offline sync | current mobile sync APIs/services | idempotent; retry-safe; tenant-safe; late-close conflicts explicit | Sync status/queue | Hybrid | Required | active lease | Audited |
| Printing | web receipts/reports | preserve tenant/financial data | 58/80mm + A4 native printing | Local | No | module permission | Planned |
| Updater | platform responsibility | signed/checksummed release metadata required | Updater | Cloud | Check | min version | Planned |
