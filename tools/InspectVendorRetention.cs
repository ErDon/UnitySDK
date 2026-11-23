using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;

// Evidence inventory, not a pass/fail claim about final JavaScript or Wasm.
class InspectVendorRetention {
    static int Main(string[] args) {
        if (args.Length != 2) {
            Console.Error.WriteLine("Usage: InspectVendorRetention <ManagedStripped directory> <assembly names, comma separated>");
            return 2;
        }
        try {
            var names = args[1].Split(',');
            if (names.Any(n => string.IsNullOrWhiteSpace(n) || n.IndexOfAny(new[]{'/', '\\', ':'}) >= 0 || n == ".."))
                throw new ArgumentException("Expected assembly names, not paths");
            // Validate every requested input before producing an inventory.
            var files = names.Select(n => Path.Combine(args[0], n + ".dll")).ToArray();
            foreach (var file in files) if (!File.Exists(file)) throw new FileNotFoundException("Requested assembly missing", file);
            Console.WriteLine("SCOPE\tlinked-managed-metadata-only; presence does not prove reachable execution or final payload size");
            foreach (var file in files) using (var assembly = AssemblyDefinition.ReadAssembly(file)) {
                var types = assembly.MainModule.GetTypes().Where(t => t.Name != "<Module>").OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
                string hash;
                using (var sha = SHA256.Create()) using (var stream = File.OpenRead(file))
                    hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                Console.WriteLine("ASSEMBLY\t" + assembly.Name.Name + "\tsha256=" + hash + "\ttypes=" + types.Length + "\tmethods=" + types.Sum(t => t.Methods.Count));
                foreach (var type in types) {
                    Console.WriteLine("TYPE\t" + type.FullName);
                    foreach (var method in type.Methods.OrderBy(m => m.FullName, StringComparer.Ordinal))
                        Console.WriteLine("METHOD\t" + method.FullName + "\tbody=" + method.HasBody);
                }
            }
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
