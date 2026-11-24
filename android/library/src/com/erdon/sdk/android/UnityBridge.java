package com.erdon.sdk.android;

import android.app.Activity;
import com.erdon.sdk.NativeSdk;
import org.json.JSONObject;
import java.lang.ref.WeakReference;
import java.util.*;
import java.util.concurrent.ConcurrentHashMap;

/** Versioned JSON boundary, independent of Unity classes and UnitySendMessage/GameObject names. */
public final class UnityBridge implements AutoCloseable {
    public interface Callback { void complete(String json); }
    public interface Logger { void write(String line); }
    private static volatile AndroidSdk.ChannelFactory channels;
    /** Install at application startup. Existing SDK instances retain their factory snapshot. */
    public static void installChannels(AndroidSdk.ChannelFactory factory) { channels=factory; }
    public static int protocolVersion() { return 1; }
    private final WeakReference<Activity> activity;
    private final NativeSdk sdk;
    private final Map<String,NativeSdk.Operation> pending=new ConcurrentHashMap<>();
    private volatile boolean closed;

    public UnityBridge(Activity activity, String gameId, String environment, String modules, Logger logger) {
        if(activity==null || gameId==null || gameId.trim().isEmpty() || gameId.length()>256 ||
            !("test".equals(environment)||"production".equals(environment))) throw new IllegalArgumentException("Invalid app binding");
        this.activity=new WeakReference<>(activity);
        EnumSet<NativeSdk.Module> enabled=EnumSet.noneOf(NativeSdk.Module.class);
        for(String name:modules.split(",")) if(!name.isEmpty()) enabled.add(NativeSdk.Module.valueOf(name));
        AndroidSdk.ChannelFactory snapshot=channels;
        long timeoutMs=enabled.contains(NativeSdk.Module.ADS)?120000:enabled.contains(NativeSdk.Module.PAYMENTS)?60000:15000;
        sdk=AndroidSdk.create(activity,gameId,environment,enabled,snapshot,logger==null?null:logger::write,timeoutMs);
    }

    private static String encode(NativeSdk.Result<?> result) {
        try {
            JSONObject json=new JSONObject().put("schemaVersion",1).put("success",result.success()).put("callId",result.callId);
            if(!result.success()) json.put("code",result.code).put("reason",result.reason).put("platformCode",result.platformCode);
            else if(result.value instanceof Boolean) json.put("completed",result.value);
            else if(result.value instanceof NativeSdk.HttpResponse) {
                NativeSdk.HttpResponse response=(NativeSdk.HttpResponse)result.value;
                json.put("status",response.status).put("value",response.body);
            } else json.put("value",result.value==null?JSONObject.NULL:result.value);
            return json.toString();
        } catch(Exception ignored) { return "{\"schemaVersion\":1,\"success\":false,\"code\":\"BRIDGE.INVALID_RESPONSE\",\"reason\":\"Cannot encode result\"}"; }
    }
    public String storage(String operation,String key,String value) { return encode(sdk.storage(operation,key,value)); }
    public void call(String id,String method,String payload,Callback callback) {
        if(id==null || callback==null) throw new IllegalArgumentException("Call ID and callback required");
        if(closed) { callback.complete(encode(NativeSdk.Result.fail("SDK.DISPOSED","Bridge closed"))); return; }
        if(pending.putIfAbsent(id,()->{})!=null) throw new IllegalArgumentException("Duplicate call ID");
        Activity host=activity.get();
        if(host==null || host.isFinishing()) { finish(id,callback,NativeSdk.Result.fail("ANDROID.ACTIVITY_UNAVAILABLE","Activity unavailable")); return; }
        host.runOnUiThread(()-> {
            if(closed || !pending.containsKey(id)) return;
            try {
                if(payload==null || payload.length()>131072) throw new IllegalArgumentException();
                JSONObject data=new JSONObject(payload);
                NativeSdk.Operation operation;
                switch(method) {
                    case "login": operation=sdk.login(r->finish(id,callback,r));break;
                    case "share": operation=sdk.share(data.getString("title"),data.getString("text"),r->finish(id,callback,r));break;
                    case "ads": operation=sdk.showAd(data.getString("unitId"),r->finish(id,callback,r));break;
                    case "payment": operation=sdk.launchPayment(data.getString("orderId"),data.getString("parametersJson"),r->finish(id,callback,r));break;
                    case "analytics": operation=sdk.report(data.getString("name"),data.getString("parametersJson"),r->finish(id,callback,r));break;
                    case "network":
                        Map<String,String> headers=new LinkedHashMap<>();JSONObject inputHeaders=data.optJSONObject("headers");
                        if(inputHeaders!=null) for(Iterator<String> keys=inputHeaders.keys();keys.hasNext();) {String key=keys.next();headers.put(key,inputHeaders.getString(key));}
                        operation=sdk.send(new NativeSdk.HttpRequest(data.getString("url"),data.getString("method"),data.optString("body",""),headers),r->finish(id,callback,r));break;
                    default: finish(id,callback,NativeSdk.Result.fail("BRIDGE.UNKNOWN_METHOD","Unknown method"));return;
                }
                // Synchronous providers may complete before invoke returns. Do not resurrect their handle.
                if(pending.replace(id,operation)==null) operation.cancel();
            } catch(Exception error) { finish(id,callback,NativeSdk.Result.fail("BRIDGE.INVALID_REQUEST","Invalid native request")); }
        });
    }
    private void finish(String id,Callback callback,NativeSdk.Result<?> result) {
        if(pending.remove(id)==null || closed) return;
        callback.complete(encode(result));
    }
    /** Cancellation releases observation; cannot undo a platform payment or a sent event. */
    public void cancel(String id) { NativeSdk.Operation operation=pending.remove(id); if(operation!=null) operation.cancel(); }
    public void close() { if(closed)return;closed=true;pending.clear();sdk.close();activity.clear(); }

}
