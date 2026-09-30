namespace BusinessOS.Pharmacy.Updater;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 2 &&
            string.Equals(args[0], "--apply", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var result = await UpdateApplier.ApplyAsync(args[1]);
                Console.WriteLine(result.Message);
                return result.Success ? 0 : 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 2;
            }
        }

        Console.Error.WriteLine("Usage: BusinessOS.Pharmacy.Updater --apply <plan.json>");
        return 64;
    }
}
