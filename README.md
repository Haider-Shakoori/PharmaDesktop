# BusinessOS Pharmacy Desktop

Native C#/.NET Windows client for the BusinessOS Pharmacy ecosystem.

The Laravel SaaS platform remains authoritative for tenant provisioning, trials, subscriptions, licensing, entitlements, device management, and cloud APIs. This repository contains the native pharmacy-management experience plus its local offline infrastructure.

## Foundation

- .NET 10
- WPF + MVVM
- Microsoft.Extensions hosting/configuration/DI
- CommunityToolkit.Mvvm
- EF Core + SQLite
- Serilog
- xUnit
- clean architecture
- Windows-first, self-contained deployment target: win-x64

The audited Laravel business rules and conversion scope are documented under docs/.
