package com.erdon.sdk.android;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.util.Log;
import com.erdon.sdk.NativeSdk;
import com.erdon.sdk.HttpsNetwork;
import java.lang.ref.WeakReference;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;

/** Native Android entry point: typed capabilities without Unity or a JSON bridge. */
public final class AndroidSdk {
    public interface ChannelFactory {
        NativeSdk.Provider create(Activity activity, String gameId, String environment, NativeSdk.Module module);
    }
    private AndroidSdk() { }
    public static NativeSdk create(Activity activity,String gameId,String environment,Set<NativeSdk.Module> modules,
                                   ChannelFactory channels,NativeSdk.LogSink logger) {
        return create(activity,gameId,environment,modules,channels,logger,15000);
    }
    public static NativeSdk create(Activity activity,String gameId,String environment,Set<NativeSdk.Module> modules,
                                   ChannelFactory channels,NativeSdk.LogSink logger,long timeoutMs) {
        if(activity==null || gameId==null || gameId.trim().isEmpty() || gameId.length()>256 ||
            !("test".equals(environment)||"production".equals(environment))) throw new IllegalArgumentException("Invalid app binding");
        WeakReference<Activity> reference=new WeakReference<>(activity);
        Context context=activity.getApplicationContext();String namespace=digest(gameId+"\n"+environment);
        return new NativeSdk(modules,module -> {
            Activity host=reference.get();
            if(channels!=null && host!=null) {
                NativeSdk.Provider custom=channels.create(host,gameId,environment,module);
                if(custom!=null) return custom;
            }
            if(module==NativeSdk.Module.STORAGE) return new Preferences(context,namespace);
            if(module==NativeSdk.Module.SHARE) return new TextShare(reference);
            if(module==NativeSdk.Module.NETWORK) return new HttpsNetwork();
            return null;
        }, runnable -> {
            Activity host=reference.get();
            if(host==null || host.isFinishing()) throw new IllegalStateException("Activity unavailable");
            host.runOnUiThread(runnable);
        },logger,line -> Log.i("ErDonSDK",line),timeoutMs);
    }
    private static String digest(String value) {
        try {
            byte[] bytes=MessageDigest.getInstance("SHA-256").digest(value.getBytes(StandardCharsets.UTF_8));
            StringBuilder text=new StringBuilder();for(byte b:bytes) text.append(String.format(Locale.ROOT,"%02x",b&255));return text.toString();
        } catch(Exception error) { throw new IllegalStateException("SHA-256 unavailable"); }
    }
    private static final class Preferences implements NativeSdk.Storage {
        private final SharedPreferences preferences;
        Preferences(Context context,String namespace) { preferences=context.getSharedPreferences("erdon."+namespace,Context.MODE_PRIVATE); }
        public NativeSdk.Result<String> get(String key) {
            return preferences.contains(key)?NativeSdk.Result.ok(preferences.getString(key,"")):NativeSdk.Result.fail("STORAGE.NOT_FOUND","Key does not exist");
        }
        // commit reports disk persistence failure; apply cannot provide that success contract.
        public NativeSdk.Result<String> set(String key,String value) { return written(preferences.edit().putString(key,value).commit()); }
        public NativeSdk.Result<String> remove(String key) { return written(preferences.edit().remove(key).commit()); }
        private NativeSdk.Result<String> written(boolean ok) { return ok?NativeSdk.Result.ok(null):NativeSdk.Result.fail("STORAGE.WRITE_FAILED","Storage commit failed"); }
        public void close() { } // Never delete player data during SDK shutdown.
    }
    private static final class TextShare implements NativeSdk.Share {
        private final WeakReference<Activity> activity;
        TextShare(WeakReference<Activity> activity) { this.activity=activity; }
        public void share(String title,String text,NativeSdk.Callback<Void> callback) {
            Activity host=activity.get();
            if(host==null || host.isFinishing()) { callback.complete(NativeSdk.Result.fail("ANDROID.ACTIVITY_UNAVAILABLE","Activity unavailable"));return; }
            try {
                Intent intent=new Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_SUBJECT,title).putExtra(Intent.EXTRA_TEXT,text);
                host.startActivity(Intent.createChooser(intent,title));
                callback.complete(NativeSdk.Result.ok(null));
            } catch(Exception error) { callback.complete(NativeSdk.Result.fail("SHARE.LAUNCH_FAILED","No share activity available")); }
        }
        public void close() { }
    }
}
