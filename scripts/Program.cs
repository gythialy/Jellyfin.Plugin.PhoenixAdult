using System;
using System.Linq;
using System.Reflection;

class Program
{
    // dotnet run ../Jellyfin.Plugin.PhoenixAdult/bin/Release/net8.0/PhoenixAdult.dll
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Please provide the assembly path as a command-line argument.");
            return;
        }

        string assemblyPath = args[0];
        try
        {
            var assembly = Assembly.LoadFrom(assemblyPath);
            Console.WriteLine($"Assembly Information for: {assemblyPath}");
            Console.WriteLine(new string('-', 50));
            Console.WriteLine($"Name: {assembly.GetName().Name}");
            Console.WriteLine($"Version: {assembly.GetName().Version}");
            Console.WriteLine($"Full Name: {assembly.FullName}");
            Console.WriteLine($"Location: {assembly.Location}");
            Console.WriteLine($"Code Base: {assembly.CodeBase}");

            var assemblyName = assembly.GetName();
            Console.WriteLine($"Culture: {assemblyName.CultureInfo.DisplayName}");
            Console.WriteLine($"Public Key Token: {BitConverter.ToString(assemblyName.GetPublicKeyToken()).Replace("-", "").ToLowerInvariant()}");

            Console.WriteLine("\nReferenced Assemblies:");
            foreach (var referencedAssembly in assembly.GetReferencedAssemblies())
            {
                Console.WriteLine($" - {referencedAssembly.FullName}");
            }

            Console.WriteLine("\nExported Types:");
            foreach (var type in assembly.GetExportedTypes().Take(5)) // Limiting to first 5 for brevity
            {
                Console.WriteLine($" - {type.FullName}");
            }
            if (assembly.GetExportedTypes().Length > 5)
            {
                Console.WriteLine($" ... and {assembly.GetExportedTypes().Length - 5} more");
            }

            var customAttributes = assembly.GetCustomAttributes(false);
            if (customAttributes.Any())
            {
                Console.WriteLine("\nCustom Attributes:");
                foreach (var attr in customAttributes.Take(5)) // Limiting to first 5 for brevity
                {
                    Console.WriteLine($" - {attr.GetType().Name}");
                }
                if (customAttributes.Length > 5)
                {
                    Console.WriteLine($" ... and {customAttributes.Length - 5} more");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
