using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

// Inspect compiled call sites, including aliases, delegates and compiler-generated methods.
// Does not certify native plugins, JavaScript, reflection or vendor assemblies.
class CheckWebGlThreads
{
    static bool Forbidden(MethodReference method)
    {
        string type = method.DeclaringType.GetElementType().FullName, name = method.Name;
        if (type == "System.Threading.Thread") return new[] { ".ctor", "Start", "Join", "Sleep", "SpinWait" }.Contains(name);
        if (type == "System.Threading.ThreadPool" || type == "System.Threading.Tasks.Parallel") return true;
        if (type == "System.Threading.Timer" || type == "System.Timers.Timer") return true;
        if (type == "System.Threading.Monitor") return name == "Wait";
        if (type == "System.Threading.CancellationTokenSource") return name == "CancelAfter" || name == ".ctor" && method.Parameters.Count > 0;
        if (type.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal))
            return new[] { "Run", "Start", "StartNew", "Wait", "WaitAll", "WaitAny", "Delay", "get_Result" }.Contains(name);
        if (type.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal)) return name == "get_Result";
        if (type == "System.Threading.WaitHandle" || type == "System.Threading.ManualResetEvent" ||
            type == "System.Threading.AutoResetEvent" || type == "System.Threading.EventWaitHandle" ||
            type == "System.Threading.Mutex" || type == "System.Threading.Semaphore") return name.StartsWith("Wait", StringComparison.Ordinal);
        if (type == "System.Threading.SemaphoreSlim" || type == "System.Threading.ManualResetEventSlim" ||
            type == "System.Threading.CountdownEvent") return name == "Wait";
        if (type == "System.Threading.Barrier") return name == "SignalAndWait";
        return false;
    }
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new Exception("Usage: CheckWebGlThreads <player assembly directory>");
            int count = 0, violations = 0;
            foreach (string file in Directory.GetFiles(args[0], "*.dll").OrderBy(x => x))
            {
                if (file.EndsWith(".ref.dll", StringComparison.OrdinalIgnoreCase)) continue;
                using (var assembly = AssemblyDefinition.ReadAssembly(file))
                {
                    string name = assembly.Name.Name;
                    if (!(name == "ErDon.UnitySDK" || name.StartsWith("ErDon.UnitySDK.", StringComparison.Ordinal)) || name.EndsWith(".Editor")) continue;
                    if (name == "ErDon.UnitySDK.Android") throw new Exception("Android assembly present in WebGL input");
                    count++;
                    foreach (var method in assembly.MainModule.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
                        foreach (var instruction in method.Body.Instructions)
                        {
                            var called = instruction.Operand as MethodReference;
                            if (called == null || !Forbidden(called)) continue;
                            Console.Error.WriteLine("WEBGL.THREAD_CALL: " + method.FullName + " -> " + called.FullName);
                            violations++;
                        }
                }
            }
            if (count == 0) throw new Exception("No SDK runtime assemblies found; refusing empty audit");
            Console.WriteLine("WEBGL_THREAD_AUDIT assemblies=" + count + " violations=" + violations);
            return violations == 0 ? 0 : 1;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 2; }
    }
}
