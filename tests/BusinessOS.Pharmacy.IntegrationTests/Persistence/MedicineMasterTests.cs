using BusinessOS.Pharmacy.Application.Abstractions.Authentication;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;
using BusinessOS.Pharmacy.Application.Abstractions.Persistence;
using BusinessOS.Pharmacy.Infrastructure;
using BusinessOS.Pharmacy.Infrastructure.Storage;
using BusinessOS.Pharmacy.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessOS.Pharmacy.IntegrationTests.Persistence;

public sealed class MedicineMasterTests
{
    [Fact]
    public async Task Starter_seed_adds_common_medicines_once_without_batches()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-medicine");

            var seeds = provider.GetRequiredService<IMedicineSeedService>();
            var first = await seeds.SeedDefaultsOnceAsync();
            var second = await seeds.SeedDefaultsOnceAsync();

            Assert.Equal(12, first);
            Assert.Equal(0, second);

            var catalog = provider.GetRequiredService<IMedicineCatalogService>();
            var medicines = await catalog.SearchAsync(new MedicineSearchFilter(Take: 100));

            Assert.Equal(12, medicines.Count);
            Assert.Contains(medicines, x => x.BrandName == "Paracetamol");
            Assert.Contains(medicines, x => x.BrandName == "Amoxicillin");
            Assert.Contains(medicines, x => x.BrandName == "Oral Rehydration Salts");

            var paracetamol = await catalog.GetAsync(
                medicines.Single(x => x.BrandName == "Paracetamol").Id);

            Assert.NotNull(paracetamol);
            Assert.Equal("500 mg", paracetamol!.Strength);
            Assert.Equal("Tablet", paracetamol.DosageForm);
            Assert.Equal("box", paracetamol.PurchaseUnit);
            Assert.Equal("tablet", paracetamol.SaleUnit);
            Assert.Equal(100m, paracetamol.UnitsPerPurchaseUnit);
            Assert.True(paracetamol.BatchTrackingRequired);
            Assert.True(paracetamol.ExpiryTrackingRequired);
            Assert.Null(paracetamol.Barcode);
            Assert.Null(paracetamol.ManufacturerId);

            var references = await catalog.GetReferenceDataAsync();
            Assert.Equal(8, references.Categories.Count);
            Assert.Empty(references.Manufacturers);

            var paths = provider.GetRequiredService<ApplicationPaths>();
            await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath}");
            await connection.OpenAsync();

            await using var tableCheck = connection.CreateCommand();
            tableCheck.CommandText = """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table' AND name = 'product_batches';
                """;

            Assert.Equal(0L, Convert.ToInt64(await tableCheck.ExecuteScalarAsync()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Individual_medicine_registration_supports_create_search_and_edit()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-medicine");

            var catalog = provider.GetRequiredService<IMedicineCatalogService>();
            var categoryId = await catalog.CreateCategoryAsync("Vitamins");
            var manufacturerId = await catalog.CreateManufacturerAsync(
                "Kabul Example Pharma",
                "Afghanistan");

            var id = await catalog.CreateAsync(new SaveMedicineRequest(
                categoryId,
                manufacturerId,
                "VIT-C-500",
                "1234567890123",
                "Vitamin C",
                "Ascorbic Acid",
                "500 mg",
                "Tablet",
                "box",
                "tablet",
                100,
                25,
                false,
                true,
                true,
                true,
                "Manual registration test"));

            var created = await catalog.GetAsync(id);
            Assert.NotNull(created);
            Assert.Equal("Vitamin C", created!.BrandName);
            Assert.Equal("1234567890123", created.Barcode);

            await catalog.UpdateAsync(id, created.ToRequest() with
            {
                Strength = "1000 mg",
                ReorderLevel = 40,
                Notes = "Updated locally",
            });

            var updated = await catalog.GetAsync(id);
            Assert.Equal("1000 mg", updated!.Strength);
            Assert.Equal(40m, updated.ReorderLevel);

            var search = await catalog.SearchAsync(new MedicineSearchFilter("Ascorbic"));
            Assert.Single(search);
            Assert.Equal(id, search[0].Id);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                catalog.CreateAsync(created.ToRequest()));

            await Assert.ThrowsAsync<ArgumentException>(() =>
                catalog.CreateAsync(created.ToRequest() with
                {
                    MedicineCode = "VIT-C-NEW",
                }));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Csv_template_preview_and_import_are_validated_and_do_not_overwrite_duplicates()
    {
        var root = CreateTemporaryRoot();
        var csvPath = Path.Combine(root, "medicines.csv");
        var templatePath = Path.Combine(root, "template.csv");

        try
        {
            Directory.CreateDirectory(root);

            await using var provider = BuildProvider(root);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-medicine");

            var csv = provider.GetRequiredService<IMedicineCsvService>();
            await csv.WriteTemplateAsync(templatePath);

            var templateLines = await File.ReadAllLinesAsync(templatePath);
            Assert.Single(templateLines);
            Assert.Contains("medicine_code", templateLines[0]);
            Assert.Contains("brand_name", templateLines[0]);
            Assert.Contains("batch_tracking_required", templateLines[0]);

            var header = string.Join(",", csv.Columns);
            var content = string.Join(Environment.NewLine,
            [
                header,
                "CSV-001,Vitamin C,Ascorbic Acid,500 mg,Tablet,Vitamins,Kabul Pharma,Afghanistan,,box,tablet,100,20,false,true,true,true,\"Imported, with comma\"",
                "CSV-001,Duplicate Code,,,,Vitamins,,,,pack,unit,1,0,false,true,true,true,",
                "CSV-003,,,,,Vitamins,,,,pack,unit,1,0,false,true,true,true,"
            ]);

            await File.WriteAllTextAsync(csvPath, content);

            var preview = await csv.PreviewAsync(csvPath);
            Assert.Equal(3, preview.TotalRows);
            Assert.Equal(1, preview.ValidRows);
            Assert.Equal(2, preview.InvalidRows);
            Assert.Contains(preview.Rows, x =>
                !x.IsValid && x.Errors.Any(error => error.Contains("duplicated", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains(preview.Rows, x =>
                !x.IsValid && x.Errors.Any(error => error.Contains("brand_name", StringComparison.OrdinalIgnoreCase)));

            var imported = await csv.ImportAsync(csvPath);
            Assert.Equal(1, imported.Imported);
            Assert.Equal(2, imported.Rejected);

            var catalog = provider.GetRequiredService<IMedicineCatalogService>();
            var medicines = await catalog.SearchAsync(new MedicineSearchFilter("Vitamin C"));
            Assert.Single(medicines);

            var item = await catalog.GetAsync(medicines[0].Id);
            Assert.Equal("Ascorbic Acid", item!.GenericName);
            Assert.Equal("Imported, with comma", item.Notes);
            Assert.Null(item.Barcode);

            var references = await catalog.GetReferenceDataAsync();
            Assert.Contains(references.Categories, x => x.Name == "Vitamins");
            Assert.Contains(references.Manufacturers, x =>
                x.Name == "Kabul Pharma" && x.Country == "Afghanistan");

            var duplicatePreview = await csv.PreviewAsync(csvPath);
            Assert.Equal(0, duplicatePreview.ValidRows);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task Medicine_master_service_fails_closed_without_medicine_permission()
    {
        var root = CreateTemporaryRoot();

        try
        {
            await using var provider = BuildProvider(root, allowMedicines: false);
            await provider.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync("tenant-medicine");

            var catalog = provider.GetRequiredService<IMedicineCatalogService>();

            await Assert.ThrowsAsync<PermissionDeniedException>(() =>
                catalog.SearchAsync(new MedicineSearchFilter()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryRoot(root);
        }
    }

    private static ServiceProvider BuildProvider(
        string root,
        bool allowMedicines = true)
    {
        var services = new ServiceCollection();
        var paths = new ApplicationPaths(root);

        services.AddBusinessOSInfrastructure(paths);
        services.AddSingleton<IPermissionAuthorizer>(
            new TestPermissionAuthorizer(allowMedicines));
        services.AddBusinessOSPersistence();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string CreateTemporaryRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "darmaltoon-medicine-tests",
            Guid.NewGuid().ToString("N"));

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestPermissionAuthorizer(bool allowMedicines) : IPermissionAuthorizer
    {
        public bool HasPermission(string permission) =>
            permission != "medicines.manage" || allowMedicines;

        public void Demand(string permission)
        {
            if (!HasPermission(permission))
            {
                throw new PermissionDeniedException(permission);
            }
        }
    }
}

internal static class MedicineEditorModelTestExtensions
{
    public static SaveMedicineRequest ToRequest(this MedicineEditorModel model) => new(
        model.MedicineCategoryId,
        model.ManufacturerId,
        model.MedicineCode,
        model.Barcode,
        model.BrandName,
        model.GenericName,
        model.Strength,
        model.DosageForm,
        model.PurchaseUnit,
        model.SaleUnit,
        model.UnitsPerPurchaseUnit,
        model.ReorderLevel,
        model.PrescriptionRequired,
        model.BatchTrackingRequired,
        model.ExpiryTrackingRequired,
        model.IsActive,
        model.Notes);
}
