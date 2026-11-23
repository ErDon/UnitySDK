using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
// Inspect linked managed metadata; this is not a native/JS payload audit.
class CheckLinkedPlatform {
 static int Main(string[] args) {
  if(args.Length!=2 || !new[]{"WeChat","Douyin","TikTok"}.Contains(args[1])) {
   Console.Error.WriteLine("Usage: CheckLinkedPlatform <ManagedStripped directory> <WeChat|Douyin|TikTok>");return 2;
  }
  try {
   var target="ErDon.UnitySDK."+args[1];
   var files=Directory.GetFiles(args[0],"*.dll");
   if(files.Length==0)throw new Exception("No linked assemblies");
   bool found=false;
   foreach(var file in files)using(var assembly=AssemblyDefinition.ReadAssembly(file)) {
    var name=assembly.Name.Name;
    Console.WriteLine("ASSEMBLY "+name);
    foreach(var foreign in new[]{"WeChat","Douyin","TikTok"}.Where(x=>x!=args[1])) {
     var forbidden="ErDon.UnitySDK."+foreign;
     if(name==forbidden || assembly.MainModule.AssemblyReferences.Any(x=>x.Name==forbidden))
      throw new Exception("Foreign platform adapter retained: "+forbidden);
    }
    if(name=="ErDon.UnitySDK.Editor")throw new Exception("Editor SDK retained");
    if(name==target){
     var registration=assembly.MainModule.Types.SingleOrDefault(t=>t.FullName==target+"."+args[1]+"Registration");
     if(registration==null || !registration.Methods.Any(m=>m.Name=="Register" && m.HasBody &&
        m.CustomAttributes.Any(a=>a.AttributeType.FullName=="UnityEngine.RuntimeInitializeOnLoadMethodAttribute")))
      throw new Exception("Target runtime registration missing after linking");
     found=true;
    }
   }
   if(!found)throw new Exception("Target adapter missing after linking: "+target);
   Console.WriteLine("LINKED_PLATFORM_REGISTRATION_PASS (managed adapters only; not final JS/Wasm isolation)");return 0;
  }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
 }
}
