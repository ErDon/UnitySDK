package com.erdon.sdk;

import java.text.SimpleDateFormat;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Engine-independent aggregate. Every factory result is owned by this instance. */
public final class NativeSdk implements AutoCloseable {
    public enum Module { LOGIN, STORAGE, SHARE, PAYMENTS, ADS, NETWORK, ANALYTICS }
    public interface Provider extends AutoCloseable { void close(); }
    public interface Callback<T> { void complete(Result<T> result); }
    public interface Operation { void cancel(); }
    public interface LogSink { void write(String formattedLine); }
    /** Return a fresh provider or null for an unsupported capability. Never return a shared instance. */
    public interface Factory { Provider create(Module module); }
    public interface Login extends Provider { void login(Callback<String> callback); }
    public interface Storage extends Provider {
        Result<String> get(String key);
        Result<String> set(String key, String value);
        Result<String> remove(String key);
    }
    /** Success only means the system accepted the share request. */
    public interface Share extends Provider { void share(String title, String text, Callback<Void> callback); }
    public interface Ads extends Provider { void show(String unitId, Callback<Boolean> callback); }
    /** Launch trusted server-prepared parameters. Success NEVER authorizes delivery of game items. */
    public interface Payments extends Provider { void launch(String orderId, String parametersJson, Callback<Void> callback); }
    public interface Analytics extends Provider { void report(String name, String parametersJson, Callback<Void> callback); }
    public interface Network extends Provider { void send(HttpRequest request, Callback<HttpResponse> callback); }
    public static final class HttpRequest {
        public final String url, method, body;
        public final Map<String,String> headers;
        public HttpRequest(String url,String method,String body,Map<String,String> headers) {
            this.url=url;this.method=method;this.body=body;
            this.headers=Collections.unmodifiableMap(headers==null?new LinkedHashMap<>():new LinkedHashMap<>(headers));
        }
    }
    public static final class HttpResponse {
        public final int status;
        public final String body;
        public HttpResponse(int status, String body) { this.status = status; this.body = body; }
    }
    public static final class Result<T> {
        public final T value;
        public final String code, reason, platformCode, callId;
        private Result(T value, String code, String reason, String platformCode) {
            this(value,code,reason,platformCode,null);
        }
        private Result(T value,String code,String reason,String platformCode,String callId) {
            this.value=value; this.code=code; this.reason=reason; this.platformCode=platformCode;this.callId=callId;
        }
        private Result<T> withCallId(String id) { return new Result<T>(value,code,reason,platformCode,id); }
        public boolean success() { return code == null; }
        public static <T> Result<T> ok(T value) { return new Result<T>(value,null,null,null); }
        public static <T> Result<T> fail(String code, String safeReason) { return fail(code,safeReason,null); }
        /** Reasons must be safe for the game UI; never pass raw server bodies or credentials here. */
        public static <T> Result<T> fail(String code, String safeReason, String platformCode) {
            if(code==null || !code.matches("[A-Z][A-Z0-9_.]{0,95}")) throw new IllegalArgumentException("Invalid error code");
            return new Result<T>(null,code,safeReason,platformCode);
        }
    }
    private interface Invoke<T> { void run(Provider provider, Callback<T> callback); }
    private final EnumSet<Module> enabled;
    private final EnumMap<Module,Provider> providers = new EnumMap<>(Module.class);
    private final Set<Call<?>> calls = new HashSet<>();
    private final Factory factory;
    private final Executor dispatch;
    private final LogSink log, fallback;
    private final long timeoutMs;
    private final ScheduledExecutorService timer;
    private volatile boolean closed;
    private final ThreadLocal<Boolean> logging = new ThreadLocal<>();

    public NativeSdk(Set<Module> enabled, Factory factory, Executor callbackExecutor,
                     LogSink log, LogSink fallback, long timeoutMs) {
        if(enabled==null || factory==null || callbackExecutor==null || fallback==null || timeoutMs<1 || timeoutMs>300000)
            throw new IllegalArgumentException("Invalid SDK configuration");
        this.enabled=enabled.isEmpty()?EnumSet.noneOf(Module.class):EnumSet.copyOf(enabled);
        this.factory=factory; dispatch=callbackExecutor; this.log=log; this.fallback=fallback; this.timeoutMs=timeoutMs;
        timer=Executors.newSingleThreadScheduledExecutor(r -> { Thread t=new Thread(r,"ErDon-timeout"); t.setDaemon(true); return t; });
    }
    private synchronized Provider provider(Module module) {
        if(Boolean.TRUE.equals(logging.get())) throw new IllegalStateException("Log reentry");
        if(closed) throw new IllegalStateException("Closed");
        if(!providers.containsKey(module)) {
            Provider created=factory.create(module);
            if(closed) { if(created!=null) created.close(); throw new IllegalStateException("Closed during factory call"); }
            if(created!=null) providers.put(module,created);
            return created;
        }
        return providers.get(module);
    }
    private <T> Operation invoke(Module module, String api, String summary, Class<?> type, Callback<T> callback, Invoke<T> action) {
        if(callback==null) throw new IllegalArgumentException("Callback required");
        if(Boolean.TRUE.equals(logging.get())) throw new IllegalStateException("SDK cannot be called from its log callback");
        Call<T> call=new Call<>(api,summary,callback);
        synchronized(this) {
            if(closed) { call.finish(Result.fail("SDK.DISPOSED","SDK is closed")); return call; }
            calls.add(call);
            call.timeout=timer.schedule(() -> call.finish(Result.fail("OPERATION.TIMEOUT","Local wait timed out")),timeoutMs,TimeUnit.MILLISECONDS);
        }
        if(!enabled.contains(module)) { call.finish(Result.fail("MODULE.DISABLED","Capability is disabled")); return call; }
        try {
            Provider p=provider(module);
            if(p==null) call.finish(Result.fail("CAPABILITY.NOT_SUPPORTED","No provider installed"));
            else if(!type.isInstance(p)) call.finish(Result.fail("CONFIG.PROVIDER_MISMATCH","Provider has wrong capability type"));
            else action.run(p,call::finish);
        } catch(Exception error) { call.finish(Result.fail("PROVIDER.EXCEPTION","Provider invocation failed")); }
        return call;
    }
    private final class Call<T> implements Operation {
        final String id=UUID.randomUUID().toString(), api;
        final AtomicBoolean finished=new AtomicBoolean();
        Callback<T> callback;
        volatile Future<?> timeout;
        Call(String api,String summary,Callback<T> callback) { this.api=api;this.callback=callback;write(api,id,"Start",summary,"Pending"); }
        void finish(Result<T> result) {
            if(!finished.compareAndSet(false,true)) return;
            if(timeout!=null) timeout.cancel(false);
            final Result<T> safe=(result==null?Result.<T>fail("PROVIDER.INVALID_RESPONSE","Missing provider result"):result).withCallId(id);
            write(api,id,"Complete","{}",safe.success()?"Success":safe.code);
            try { dispatch.execute(() -> {
                Callback<T> target;
                synchronized(NativeSdk.this) { calls.remove(this); target=callback;callback=null; if(closed && !"SDK.DISPOSED".equals(safe.code)) return; }
                try { if(target!=null) target.complete(safe); } catch(Exception error) { write(api,id,"Callback","{}","CALLBACK.EXCEPTION"); }
            }); } catch(Exception error) {
                synchronized(NativeSdk.this) { calls.remove(this);callback=null; }
                write(api,id,"Dispatch","{}","DISPATCH.EXCEPTION");
            }
        }
        public void cancel() { finish(Result.fail("OPERATION.CANCELLED","Local wait cancelled; remote action may continue")); }
        void abandon() { finished.set(true); if(timeout!=null) timeout.cancel(false); callback=null; }
    }
    public Operation login(Callback<String> callback) {
        return invoke(Module.LOGIN,"login","{}",Login.class,callback,(p,c)->((Login)p).login(r -> {
            if(r!=null && r.success() && (r.value==null || r.value.trim().isEmpty())) c.complete(Result.fail("AUTH.INVALID_RESPONSE","Missing verified game session"));
            else c.complete(r);
        }));
    }
    public Operation share(String title,String text,Callback<Void> callback) {
        return invoke(Module.SHARE,"share","{\"content\":\"[REDACTED]\"}",Share.class,callback,(p,c)-> {
            if(title==null || text==null || title.length()>256 || text.length()>8192) c.complete(Result.fail("SHARE.INVALID_REQUEST","Invalid text share"));
            else ((Share)p).share(title,text,c);
        });
    }
    public Operation showAd(String unitId,Callback<Boolean> callback) {
        return invoke(Module.ADS,"showAd","{\"unitId\":\"[REDACTED]\"}",Ads.class,callback,(p,c)-> {
            if(unitId==null || unitId.trim().isEmpty()) c.complete(Result.fail("ADS.INVALID_REQUEST","Ad unit required"));
            else ((Ads)p).show(unitId,r -> {
                if(r!=null && r.success() && r.value==null) c.complete(Result.fail("ADS.INVALID_RESPONSE","Missing completion state"));
                else c.complete(r);
            });
        });
    }
    public Operation launchPayment(String orderId,String parametersJson,Callback<Void> callback) {
        return invoke(Module.PAYMENTS,"launchPayment","{\"parameters\":\"[REDACTED]\"}",Payments.class,callback,(p,c)-> {
            if(orderId==null || orderId.trim().isEmpty() || parametersJson==null || parametersJson.length()>65536) c.complete(Result.fail("PAY.INVALID_REQUEST","Prepared order required"));
            else ((Payments)p).launch(orderId,parametersJson,c);
        });
    }
    public Operation report(String name,String parametersJson,Callback<Void> callback) {
        return invoke(Module.ANALYTICS,"report","{\"parameters\":\"[REDACTED]\"}",Analytics.class,callback,(p,c)-> {
            if(name==null || !name.matches("[A-Za-z0-9_]{1,64}") || parametersJson==null || parametersJson.length()>65536) c.complete(Result.fail("ANALYTICS.INVALID_EVENT","Invalid event"));
            else ((Analytics)p).report(name,parametersJson,c);
        });
    }
    public Operation send(String url,String method,String body,Callback<HttpResponse> callback) {
        return send(new HttpRequest(url,method,body,null),callback);
    }
    public Operation send(HttpRequest request,Callback<HttpResponse> callback) {
        return invoke(Module.NETWORK,"send","{\"request\":\"[REDACTED]\"}",Network.class,callback,(p,c)-> {
            java.net.URI uri;
            try { uri=new java.net.URI(request.url); } catch(Exception error) { c.complete(Result.fail("NETWORK.INVALID_REQUEST","Invalid URL")); return; }
            if(!"https".equalsIgnoreCase(uri.getScheme()) || uri.getHost()==null || uri.getUserInfo()!=null || uri.getFragment()!=null ||
                !("GET".equals(request.method)||"POST".equals(request.method)) || (request.body!=null && request.body.length()>65536) || !validHeaders(request.headers)) c.complete(Result.fail("NETWORK.INVALID_REQUEST","Valid HTTPS GET/POST and headers required"));
            else ((Network)p).send(request,c);
        });
    }
    private static boolean validHeaders(Map<String,String> headers) {
        if(headers.size()>32)return false;
        Set<String> names=new HashSet<>();
        for(Map.Entry<String,String> header:headers.entrySet()) {
            String name=header.getKey(),value=header.getValue();
            if(name==null||!name.matches("[A-Za-z0-9-]{1,64}")||value==null||value.length()>8192||value.indexOf('\r')>=0||value.indexOf('\n')>=0)return false;
            String lower=name.toLowerCase(Locale.ROOT);
            if(!names.add(lower)||Arrays.asList("host","content-length","connection","transfer-encoding").contains(lower))return false;
        }
        return true;
    }
    public Result<String> setStorage(String key,String value) { return storage("set",key,value); }
    public Result<String> getStorage(String key) { return storage("get",key,null); }
    public Result<String> removeStorage(String key) { return storage("remove",key,null); }
    /** Transport entry; native game code should use the three named storage methods above. */
    public synchronized Result<String> storage(String operation,String key,String value) {
        if(Boolean.TRUE.equals(logging.get())) throw new IllegalStateException("Log reentry");
        String id=UUID.randomUUID().toString();write("storage."+operation,id,"Start","{\"key\":\"[REDACTED]\",\"value\":\"[REDACTED]\"}","Pending");
        Result<String> result;
        try {
            if(closed) result=Result.fail("SDK.DISPOSED","SDK is closed");
            else if(!enabled.contains(Module.STORAGE)) result=Result.fail("MODULE.DISABLED","Storage is disabled");
            else if(key==null || key.isEmpty() || key.length()>256 || ("set".equals(operation)&&(value==null || value.length()>1048576))) result=Result.fail("STORAGE.INVALID_REQUEST","Invalid key or value");
            else {
                Provider p=provider(Module.STORAGE);
                if(!(p instanceof Storage)) result=Result.fail("CAPABILITY.NOT_SUPPORTED","Storage not installed");
                else if("get".equals(operation)) result=((Storage)p).get(key);
                else if("set".equals(operation)) result=((Storage)p).set(key,value);
                else if("remove".equals(operation)) result=((Storage)p).remove(key);
                else result=Result.fail("STORAGE.INVALID_REQUEST","Unknown operation");
            }
        } catch(Exception error) { result=Result.fail("STORAGE.FAILED","Storage operation failed"); }
        if(result==null) result=Result.fail("PROVIDER.INVALID_RESPONSE","Missing storage result");
        write("storage."+operation,id,"Complete","{}",result.success()?"Success":result.code);return result.withCallId(id);
    }
    private void write(String api,String call,String stage,String params,String result) {
        if(Boolean.TRUE.equals(logging.get())) return;
        logging.set(true);
        try {
            String date=new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSSXXX",Locale.ROOT).format(new Date());
            String line=date+" platform=Android interface="+api.replaceAll("[\\r\\n]"," ")+" callId="+call+" traceId="+call+" logId="+UUID.randomUUID()+" stage="+stage+" params="+params+" result="+result;
            if(log!=null) { try { log.write(line); return; } catch(Exception ignored) { } }
            try { fallback.write(line); } catch(Exception ignored) { }
        } finally { logging.remove(); }
    }
    public synchronized void close() {
        if(Boolean.TRUE.equals(logging.get())) throw new IllegalStateException("Log reentry");
        if(closed) return;closed=true;
        for(Call<?> call:calls) call.abandon(); calls.clear();timer.shutdownNow();
        Set<Provider> unique=Collections.newSetFromMap(new IdentityHashMap<Provider,Boolean>());
        for(Provider p:providers.values()) if(unique.add(p)) try { p.close(); } catch(Exception ignored) { }
        providers.clear();
    }
}
