# Batch 3 Report

Batch: 3 — WPF shell, navigation, BusinessOS design system, RTL foundation

Status: implemented and independently verified.

Created:
- reusable WPF resource dictionaries for colors, typography, controls, and navigation
- native BusinessOS Pharmacy sidebar/topbar shell
- English, Dari, and Pashto UI language catalog
- RTL/LTR flow-direction switching foundation
- MVVM navigation item model
- localized shell navigation labels

Changed:
- App.xaml now loads shared design-system resources
- MainWindow now uses the native desktop shell instead of the foundation placeholder
- MainWindowViewModel now owns shell localization and layout direction
- CI build runner changed from windows-latest to ubuntu-latest for reliable cross-compilation with EnableWindowsTargeting

Database changes:
- none

API changes:
- none

Verification:
- dotnet restore: passed
- dotnet build Release --no-restore: passed
- build warnings: 0
- build errors: 0
- unit tests: 5 passed, 0 failed
- integration tests: 2 passed, 0 failed
- package vulnerability audit: no vulnerable packages reported

Verification environment:
- Ubuntu 24.04 development VPS
- .NET SDK 10.0.401
- WPF project cross-compiled for net10.0-windows with EnableWindowsTargeting

Known follow-ups:
- native Windows runtime/UI smoke testing remains required before installer/release batches
- the GitHub Windows-hosted runner was failing before executing any workflow steps; CI now uses the same Linux cross-build approach proven locally
- operational module pages remain scheduled for their respective batches

Next batch:
- Batch 4 — licensing backend integration.
