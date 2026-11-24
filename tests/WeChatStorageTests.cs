using System;
using System.Collections.Generic;
using ErDon.UnitySDK.WeChat;
class WeChatStorageTests
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Main(){
        var values=new Dictionary<string,string>();
        Action<string,string> set=(k,v)=>values[k]=v;
        Func<string,string,string> get=(k,d)=>values.ContainsKey(k)?values[k]:d;
        Func<string,bool> has=k=>values.ContainsKey(k);
        Action<string> remove=k=>values.Remove(k);
        // Same physical backend and logical key: game and environment must both isolate data.
        var first=new WeChatStorage("first-game","test",set,get,has,remove);
        var second=new WeChatStorage("independent-second-game","test",set,get,has,remove);
        var production=new WeChatStorage("independent-second-game","production",set,get,has,remove);
        Check(first.Set("save","first").Success && second.Set("save","second").Success && production.Set("save","production").Success,"three scopes write");
        Check(first.Get("save").Value=="first" && second.Get("save").Value=="second" && production.Get("save").Value=="production","game and environment isolation");
        second.Dispose();
        second=new WeChatStorage("independent-second-game","test",set,get,has,remove);
        Check(second.Get("save").Value=="second","second game reopen retains its own value");
        Check(second.Remove("save").Success && first.Get("save").Value=="first" && production.Get("save").Value=="production","delete cannot affect other scopes");
        first.Dispose();second.Dispose();production.Dispose();
        var a=new WeChatStorage("a:b","test",set,get,has,remove);
        var b=new WeChatStorage("a","b:test",set,get,has,remove);
        Check(a.Set("private-key","").Success && a.Get("private-key").Value=="","empty preserved");
        Check(b.Get("private-key").Error.Code=="STORAGE.NOT_FOUND","scope separators cannot collide");
        Check(a.Set("private-key","中文\nvalue").Success,"unicode write"); a.Dispose();
        a=new WeChatStorage("a:b","test",set,get,has,remove);
        Check(a.Get("private-key").Value=="中文\nvalue","dispose preserves persisted data");
        Check(a.Remove("private-key").Success && a.Remove("private-key").Success,"idempotent remove");
        var ignoredWrite=new WeChatStorage("g","e",(k,v)=>{},get,has,remove);
        Check(ignoredWrite.Set("key","value").Error.Code=="STORAGE.WRITE_FAILED","silent write failure detected");
        a.Set("private-key","v");
        var ignoredRemove=new WeChatStorage("a:b","test",set,get,has,k=>{});
        Check(ignoredRemove.Remove("private-key").Error.Code=="STORAGE.REMOVE_FAILED","silent removal failure detected");
        var invalidRead=new WeChatStorage("a:b","test",set,(k,d)=>null,has,remove);
        Check(invalidRead.Get("private-key").Error.Code=="STORAGE.READ_FAILED","null read is not success");
        var foreignRead=new WeChatStorage("a:b","test",set,(k,d)=>"foreign-format",has,remove);
        Check(foreignRead.Get("private-key").Error.Code=="STORAGE.FORMAT_MISMATCH","foreign format not silently read");
        var throwing=new WeChatStorage("g","e",(k,v)=>{throw new Exception("secret");},get,has,remove);
        Check(throwing.Set("k","v").Error.Code=="STORAGE.PROVIDER_FAILED","safe exception");
        a.Dispose();Check(a.Get("k").Error.Code=="SDK.DISPOSED","disposed provider rejected");
        Console.WriteLine("WECHAT_STORAGE_TESTS_PASS (compiled WX entry points; injected storage behavior, not device verification)");
    }
}
