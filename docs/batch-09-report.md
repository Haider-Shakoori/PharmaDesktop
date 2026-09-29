# Batch 9 Report

Batch: 9 — Medicines / products / categories

Status: implemented and verified, with the verification-environment notes below.

## Brand rename

The native Windows product is now branded as **Darmaltoon**.

- visible application name: Darmaltoon
- public brand/domain reference: darmaltoon.com
- parent/platform brand remains BusinessOS.af
- desktop assembly/output name changed from BusinessOS.Pharmacy.Desktop to Darmaltoon
- internal namespaces and repository structure remain BusinessOS.Pharmacy.* to avoid unnecessary architectural churn
- licensing API BaseUrl was intentionally not changed in this batch; activation must not be broken before the new domain is actually deployed/configured for the SaaS backend

Runtime branding was updated in:
- activation window and activation prompt
- staff login
- main window/sidebar
- dashboard fallback brand
- local-database safety dialog
- global exception dialog
- structured log application name

## Medicine master schema

A generated EF Core migration adds Laravel-compatible local master tables:

- medicine_categories
- manufacturers
- medicines

Column names intentionally match Laravel snake_case, including:
- medicine_category_id
- manufacturer_id
- medicine_code
- brand_name
- generic_name
- dosage_form
- purchase_unit
- sale_unit
- units_per_purchase_unit
- reorder_level
- prescription_required
- batch_tracking_required
- expiry_tracking_required
- is_active
- created_at
- updated_at

The migration includes:
- category name uniqueness
- manufacturer name uniqueness
- medicine-code uniqueness
- nullable unique barcode
- brand/generic/status/category indexes
- nullable category/manufacturer foreign keys with SET NULL behavior

No product_batches, stock movements, quantities, expiry lots or inventory records are created in Batch 9.
Those remain Batch 10.

## Individual medicine registration

The native Medicines workspace now supports:

- medicine search
- category filter
- active/inactive filter
- individual medicine creation
- medicine editing
- automatic next MED-#### code suggestion
- optional barcode
- brand name
- generic name
- strength
- dosage form
- category
- manufacturer
- purchase unit
- sale unit
- units per purchase unit
- reorder level
- prescription-required flag
- batch-tracking-required flag
- expiry-tracking-required flag
- active/inactive flag
- notes

Validation follows the current Laravel medicine-master limits and defaults.

The application service enforces medicines.manage; hiding UI controls is not the security boundary.

## Categories and manufacturers

The Medicines screen allows local creation of:

- medicine categories
- manufacturers
- optional manufacturer country

Duplicate names are treated case-insensitively.

## CSV template and import

Darmaltoon can generate a CSV template directly from the Medicines screen.

Template columns:

- medicine_code
- brand_name
- generic_name
- strength
- dosage_form
- category
- manufacturer
- manufacturer_country
- barcode
- purchase_unit
- sale_unit
- units_per_purchase_unit
- reorder_level
- prescription_required
- batch_tracking_required
- expiry_tracking_required
- is_active
- notes

Import flow:

1. choose CSV
2. preview every row
3. see valid/rejected counts
4. see row-specific validation errors
5. import valid rows only

Safety behavior:

- missing required headers are rejected
- medicine_code and brand_name are required
- duplicate database medicine codes are rejected
- duplicate database barcodes are rejected
- duplicate codes/barcodes within the same CSV are rejected
- numeric fields are validated
- booleans accept true/false, yes/no and 1/0
- quoted commas are supported
- existing medicines are never silently overwritten
- referenced categories/manufacturers can be created automatically from valid CSV rows

## Starter medicine data

A one-time per-local-database seed adds 12 starter medicine master records and 8 categories.

Starter medicines:

1. Paracetamol — 500 mg Tablet
2. Ibuprofen — 400 mg Tablet
3. Amoxicillin — 500 mg Capsule
4. Azithromycin — 500 mg Tablet
5. Cefixime — 400 mg Tablet
6. Omeprazole — 20 mg Capsule
7. Metformin — 500 mg Tablet
8. Amlodipine — 5 mg Tablet
9. Losartan — 50 mg Tablet
10. Cetirizine — 10 mg Tablet
11. Oral Rehydration Salts — Sachet
12. Salbutamol Inhaler — 100 mcg/dose

Starter categories:

- Pain & Fever
- Antibiotics
- Gastrointestinal
- Diabetes
- Cardiovascular
- Allergy
- Respiratory
- Rehydration

The seed intentionally does **not** fabricate:

- barcodes
- manufacturers

Those optional fields remain null until the pharmacy enters/imports real information.

All seeded medicines:
- are active
- have sensible unit/reorder defaults
- retain batch tracking = true
- retain expiry tracking = true
- create no batch records
- create no stock quantity
- create no expiry lots

The seed is idempotent and marked complete through local settings so deleting medicines later does not cause the starter set to reappear on every startup.

## Navigation/UI

- sidebar Medicines navigation is now functional
- Dashboard Add medicine quick action is now functional
- main content switches natively between Dashboard and Medicines
- future module navigation remains staged until its scheduled batch
- medicine page supports the existing English/Dari/Pashto shell and RTL direction
- primary Medicines page headings/actions include localized labels

## Verification

Final native build after the Darmaltoon branding sweep:
- Release build: PASS
- warnings: 0
- errors: 0
- desktop assembly produced as Darmaltoon.dll in the cross-build; Windows apphost/publish uses the Darmaltoon assembly name

Unit tests after final branding changes:
- 12 passed
- 0 failed

Focused Batch 9 integration tests:
- 4 passed
- 0 failed

Covered:
- one-time 12-medicine seed
- 8 category seed
- no batch table introduced
- manual create/search/edit
- duplicate medicine-code rejection
- duplicate barcode rejection
- CSV template
- CSV preview validation
- valid-row import
- quoted-comma parsing
- automatic CSV category/manufacturer creation
- permission fail-closed behavior

Full integration suite after the Batch 9 persistence/schema/test changes:
- 12 passed
- 0 failed

The existing dashboard parity fixture was updated to seed the real Batch 9 medicines table rather than creating a shadow medicines table.

A later all-in-one verification command, after only three desktop branding-string changes, encountered a VPS xUnit TestHost OutOfMemoryException during test discovery. This was an environment/process failure, not a failing assertion. The final build and unit suite in that same command passed before the OOM, and the integration project does not reference the desktop branding files changed afterward.

Dependency note:
- Batch 9 adds no new NuGet package references
- the package graph is unchanged from the previously clean Batch 8 vulnerability audit
- a fresh audit could not be rerun after the final OOM because the remote verification device disconnected

EF note:
- the MedicineMaster migration and model snapshot were generated directly by EF Core after correcting all medicine-master columns to Laravel-compatible snake_case
- the remote verification device disconnected before a final separate has-pending-model-changes command could be rerun

GitHub Actions note:
- latest branch run: 36633609358
- conclusion: failure
- job returned steps: null
- this matches the previously documented GitHub runner failure-before-steps behavior and exposes no application test failure

## Next batch

Batch 10 — Inventory / batches / expiry.

That batch will introduce stock locations, product batches/lots, expiry handling and inventory quantities on top of the medicine master created here.
