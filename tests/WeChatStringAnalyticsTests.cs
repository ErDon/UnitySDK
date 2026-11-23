using System;
using System.Collections.Generic;
using ErDon.UnitySDK;
using ErDon.UnitySDK.WeChat;
class WeChatStringAnalyticsTests {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static void Main(){
  int calls=0;
  var provider=new WeChatAnalytics((name,data)=>{calls++;Check(name=="event","name");Check(data.Count==0||(string)data["mode"]=="solo","string payload");data.Clear();});
  foreach(var value in new object[]{3,2.5}){
   var result=provider.ReportEvent(new AnalyticsEvent("event",new Dictionary<string,object>{{"value",value}}));
   Check(!result.Dispatched&&result.Error.Code=="ANALYTICS.PARAMETER_TYPE_UNSUPPORTED"&&calls==0,"numeric must not dispatch or convert");
  }
  var item=new AnalyticsEvent("event",new Dictionary<string,object>{{"mode","solo"}});
  Check(provider.ReportEvent(item).Dispatched&&calls==1&&item.Parameters.Count==1,"strings and snapshot");
  Check(provider.ReportEvent(new AnalyticsEvent("event")).Dispatched&&calls==2,"empty event");
  provider.Dispose();Check(provider.ReportEvent(item).Error.Code=="SDK.DISPOSED"&&calls==2,"disposed");
  Console.WriteLine("WECHAT_STRING_ANALYTICS_PASS (injected dispatch; official signature compiled)");
 }
}
