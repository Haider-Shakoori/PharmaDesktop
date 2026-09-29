# Batch 8 Report

Batch: 8 — Dashboard

Status: implemented and independently verified.

Source-of-truth audit:
- native Batch 8 follows the current Laravel pharmacy dashboard and OperationalAlertService behavior
- dashboard metrics preserved:
  - today's completed sales
  - low-stock medicine count
  - near-expiry batch count
  - total operational alerts
  - today's completed transaction count
  - active stock value at purchase cost
  - active customer count
  - outstanding credit on completed sales
- alert categories preserved:
  - low stock
  - near expiry
  - expired
- Laravel inventory-policy defaults preserved:
  - low_stock_threshold = 10
  - near_expiry_days = 90
- business-date fallback follows the current midnight rollover default and tenant timezone

Native dashboard:
- replaced the Batch 6 role/workspace placeholder with a real WPF dashboard surface
- pharmacy header shows subscription health, pharmacy name, pharmacy code, signed-in staff user and business date
- eight dashboard statistic cards mirror the current Laravel dashboard
- local Refresh action reloads the dashboard without requiring an API request
- attention panel summarizes low-stock, near-expiry and expired inventory
- top operational alert rows can be rendered from the same local snapshot
- zero-alert state is handled separately
- zero-module-data state is explicit and safe
- English, Dari and Pashto labels are supported through the existing UI language switcher

Local-first behavior:
- ILocalDashboardQueryService lives behind the Application abstraction
- LocalDashboardQueryService reads SQLite directly through the persistence boundary
- no network request is performed when the dashboard refreshes
- the query layer detects whether later module tables exist
- before Batches 9-13 create medicine/inventory/sales/customer tables, dashboard cards return a clean zero-state
- as those local tables arrive, the dashboard automatically begins calculating the corresponding cards

Metric parity details:
- today's sales counts completed sales for the resolved business date
- today's transactions counts completed sales for the resolved business date
- outstanding credit mirrors Laravel by summing due_total across completed sales
- stock value mirrors Laravel by summing active positive batch quantity multiplied by purchase_cost
- active customers count is_active customers
- sellable low-stock quantity excludes expired batches
- medicine reorder_level overrides the default low-stock threshold when greater than zero
- near-expiry counts active positive batches from business date through business date + 90 days
- expired counts active positive batches before the business date

Tenant metadata:
- activation state now retains the non-secret tenant/plan/subscription metadata already returned by the Laravel license API
- stored values include pharmacy display name, slug/code, timezone, currency and subscription-health label
- the signed entitlement remains the authority for tenant/device/license validation
- older cached activation states remain compatible because the added display metadata is optional

Permission behavior:
- dashboard content requires dashboard.view
- quick actions are filtered using the exact Laravel permission codes
- staged native modules are shown only for permitted users and remain disabled until their scheduled module batch lands
- this avoids presenting unfinished POS/medicine/customer/purchase/report workflows as operational

Verification:
- Release build: passed
- build warnings: 0
- build errors: 0
- unit tests: 12 passed, 0 failed
- integration tests: 8 passed, 0 failed
- NuGet vulnerability audit: no vulnerable packages reported
- verification environment: Ubuntu 24.04 / .NET SDK 10.0.401 / net10.0-windows cross-build

Dashboard integration coverage:
- clean zero-state before operational module tables exist
- file-backed SQLite dashboard query using Laravel-compatible table/column names
- seeded parity fixture verifies:
  - today's sales = 150
  - today's transactions = 2
  - stock value = 72
  - active customers = 2
  - outstanding credit = 25
  - low stock = 2
  - near expiry = 1
  - expired = 1
  - total alerts = 4
- low-stock and expiry detail rows are also verified

GitHub Actions note:
- branch run 36628522358 failed before any workflow steps were exposed
- GitHub returned steps: null
- this is the same non-diagnostic runner behavior previously documented
- the exact branch code was independently compiled, tested and audited successfully, so this is not recorded as an application-test failure

Known follow-ups:
- native Windows visual smoke testing remains required before release packaging
- quick actions will be enabled when their corresponding native modules are implemented
- pharmacy-settings synchronization can later replace the audited 10/90 inventory defaults with tenant-specific local settings
- dashboard queries are deliberately compatible with the local module tables scheduled next

Next batch:
- Batch 9 — Medicines / products / categories.
