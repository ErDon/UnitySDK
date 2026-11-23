using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
// Inspect linked managed metadata; this is not a native/JS payload audit.
class CheckLinkedPlatform {
 static int Main(string[] args) {
  if(args.Length<2 || args.Length>3 || !new[]{"WeChat","Douyin","TikTok"}.Contains(args[1])) {
   Console.Error.WriteLine("Usage: CheckLinkedPlatform <ManagedStripped directory> <WeChat|Douyin|TikTok> [Login,Storage,Share,Ads,Analytics,Network,Payments]");return 2;
  }
  try {
   var target="ErDon.UnitySDK."+args[1];
   var excluded=args.Length==3?args[2].Split(','):new string[0];
   var names=new System.Collections.Generic.Dictionary<string,string> {
    {"Login","CredentialProvider"},{"Storage","Storage"},{"Share","Share"},
    {"Ads","RewardedAds"},{"Analytics","Analytics"}
   };
   foreach(var module in excluded)if(!names.ContainsKey(module) && module!="Network" && module!="Payments")throw new Exception("Unknown excluded capability: "+module);
   var forbiddenTypes=excluded.Where(names.ContainsKey).Select(x=>target+"."+args[1]+names[x]).ToList();
   if(excluded.Contains("Network"))forbiddenTypes.AddRange(new[]{"ErDon.UnitySDK.UnityNetwork","ErDon.UnitySDK.NetworkProviderFactory","ErDon.UnitySDK.BoundedResponseBuffer"});
   if(excluded.Contains("Payments"))forbiddenTypes.AddRange(new[]{"ErDon.UnitySDK.VerifiedPayment","ErDon.UnitySDK.UnityOrderClient","ErDon.UnitySDK.OrderResponsePolicy"});
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
    foreach(var type in assembly.MainModule.GetTypes().Select(t=>t.FullName).Concat(assembly.MainModule.GetTypeReferences().Select(t=>t.FullName)))
     if(forbiddenTypes.Contains(type))throw new Exception("Excluded capability type or reference retained: "+type);
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
   if(excluded.Length>0)Console.WriteLine("LINKED_EXCLUDED_CAPABILITIES_PASS: "+string.Join(",",excluded));
   Console.WriteLine("LINKED_PLATFORM_REGISTRATION_PASS (managed adapters only; not final JS/Wasm isolation)");return 0;
  }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
 }
}
