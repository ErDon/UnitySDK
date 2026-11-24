import com.erdon.sdk.NativeSdk;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;

public final class NativeSdkTests {
    static void check(boolean value,String message) { if(!value) throw new AssertionError(message); }
    static class Login implements NativeSdk.Login {
        NativeSdk.Callback<String> callback;int closed;
        public void login(NativeSdk.Callback<String> callback) { this.callback=callback; }
        public void close() { closed++; }
    }
    public static void main(String[] args) throws Exception {
        List<String> logs=new ArrayList<>();AtomicInteger created=new AtomicInteger();Login provider=new Login();
        NativeSdk sdk=new NativeSdk(EnumSet.of(NativeSdk.Module.LOGIN),m->{created.incrementAndGet();return provider;},Runnable::run,null,logs::add,10000);
        check(created.get()==0,"lazy construction");
        AtomicReference<NativeSdk.Result<Void>> disabled=new AtomicReference<>();sdk.share("title","secret",disabled::set);
        check("MODULE.DISABLED".equals(disabled.get().code)&&created.get()==0,"disabled no construction");
        AtomicInteger completed=new AtomicInteger();AtomicReference<String> error=new AtomicReference<>();
        NativeSdk.Operation first=sdk.login(r->{completed.incrementAndGet();error.set(r.code);});
        first.cancel();provider.callback.complete(NativeSdk.Result.ok("late-secret-session"));
        check(completed.get()==1&&"OPERATION.CANCELLED".equals(error.get()),"cancel ignores late callback");
        sdk.login(r->{completed.incrementAndGet();check(r.success(),"login success");});
        provider.callback.complete(NativeSdk.Result.ok("secret-session"));provider.callback.complete(NativeSdk.Result.ok("duplicate"));
        check(completed.get()==2&&created.get()==1,"at most once, reuse within lifetime");
        sdk.login(r->completed.incrementAndGet());sdk.close();sdk.close();provider.callback.complete(NativeSdk.Result.ok("late"));
        check(completed.get()==2&&provider.closed==1,"close suppresses results and owns provider once");
        sdk.login(r->error.set(r.code));check("SDK.DISPOSED".equals(error.get()),"new call after close must return disposed");
        check(logs.stream().allMatch(s->s.contains("interface=")&&s.contains("callId=")&&s.contains("logId=")&&s.contains("params=")),"log contract");
        check(logs.stream().noneMatch(s->s.contains("secret")),"no credentials/content in logs");
        List<Runnable> queued=new ArrayList<>();Login queuedProvider=new Login();
        NativeSdk queueSdk=new NativeSdk(EnumSet.of(NativeSdk.Module.LOGIN),m->queuedProvider,queued::add,null,s->{},10000);
        queueSdk.login(r->{throw new AssertionError("queued result survived close");});queuedProvider.callback.complete(NativeSdk.Result.ok("session"));
        queueSdk.close();queued.forEach(Runnable::run);
        CountDownLatch timeout=new CountDownLatch(1);
        NativeSdk slow=new NativeSdk(EnumSet.of(NativeSdk.Module.LOGIN),m->new Login(),Runnable::run,null,s->{},20);
        slow.login(r->{if("OPERATION.TIMEOUT".equals(r.code))timeout.countDown();});
        check(timeout.await(2,TimeUnit.SECONDS),"bounded timeout");slow.close();
        AtomicInteger fallback=new AtomicInteger();
        NativeSdk absent=new NativeSdk(EnumSet.allOf(NativeSdk.Module.class),m->null,Runnable::run,s->{throw new RuntimeException();},s->fallback.incrementAndGet(),1000);
        AtomicReference<String> code=new AtomicReference<>();absent.login(r->code.set(r.code));
        check("CAPABILITY.NOT_SUPPORTED".equals(code.get())&&fallback.get()==2,"missing provider, log fallback");absent.close();
        NativeSdk broken=new NativeSdk(EnumSet.of(NativeSdk.Module.LOGIN),m->new Login(){public void login(NativeSdk.Callback<String> c){c.complete(null);}},Runnable::run,null,s->{},1000);
        broken.login(r->code.set(r.code));check("PROVIDER.INVALID_RESPONSE".equals(code.get()),"null provider response");broken.close();
        AtomicInteger requests=new AtomicInteger();
        NativeSdk network=new NativeSdk(EnumSet.of(NativeSdk.Module.NETWORK),m->new NativeSdk.Network(){
            public void close(){}public void send(NativeSdk.HttpRequest request,NativeSdk.Callback<NativeSdk.HttpResponse> callback){requests.incrementAndGet();callback.complete(NativeSdk.Result.ok(new NativeSdk.HttpResponse(204,"")));}
        },Runnable::run,null,s->{},1000);
        Map<String,String> headers=new HashMap<>();headers.put("Authorization","Bearer secret");
        NativeSdk.HttpRequest request=new NativeSdk.HttpRequest("https://example.com","GET","",headers);headers.put("Authorization","changed");
        check(request.headers.get("Authorization").equals("Bearer secret"),"header snapshot");
        network.send(request,r->check(r.success()&&r.callId!=null,"network result correlation"));
        headers.put("X-Header","bad\r\nInjected: value");network.send(new NativeSdk.HttpRequest("https://example.com","GET","",headers),r->code.set(r.code));
        check(requests.get()==1&&"NETWORK.INVALID_REQUEST".equals(code.get()),"header injection rejected");
        network.send("http://example.com","GET","",r->code.set(r.code));check(requests.get()==1&&"NETWORK.INVALID_REQUEST".equals(code.get()),"cleartext rejected");network.close();
        System.out.println("ANDROID_CORE_TESTS_PASS: lazy modules, ownership, cancellation, duplicate/late callbacks, queued close, timeout, unsupported, redacted logs");
    }
}
