package com.erdon.sdk;

import java.io.*;
import java.net.*;
import java.nio.ByteBuffer;
import java.nio.charset.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import javax.net.ssl.HttpsURLConnection;
import javax.net.ssl.SSLException;

/** Bounded UTF-8 HTTPS transport. Uses system trust/hostname checks; never follows redirects. */
public final class HttpsNetwork implements NativeSdk.CancellableNetwork {
    interface Connections { HttpsURLConnection open(URL url) throws IOException; }
    private final Connections connections;
    private final int timeoutMs, maxResponseBytes;
    private final ThreadPoolExecutor workers;
    private final ThreadPoolExecutor aborts;
    private final Set<Request> active=new HashSet<>();
    private boolean closed;
    public HttpsNetwork() { this(10000,1048576); }
    public HttpsNetwork(int timeoutMs,int maxResponseBytes) {
        this(timeoutMs,maxResponseBytes,url -> (HttpsURLConnection)url.openConnection());
    }
    // Package-private injection is only for deterministic transport/TLS tests. Production has no trust override.
    HttpsNetwork(int timeoutMs,int maxResponseBytes,Connections connections) {
        if(timeoutMs<1||timeoutMs>120000||maxResponseBytes<1||maxResponseBytes>4194304||connections==null)
            throw new IllegalArgumentException("Invalid HTTPS limits");
        this.timeoutMs=timeoutMs;this.maxResponseBytes=maxResponseBytes;this.connections=connections;
        workers=new ThreadPoolExecutor(2,2,0,TimeUnit.MILLISECONDS,new ArrayBlockingQueue<Runnable>(32),r->{
            Thread t=new Thread(r,"ErDon-HTTPS");t.setDaemon(true);return t;
        },new ThreadPoolExecutor.AbortPolicy());
        aborts=new ThreadPoolExecutor(2,2,0,TimeUnit.MILLISECONDS,new ArrayBlockingQueue<Runnable>(32),r->{
            Thread t=new Thread(r,"ErDon-HTTPS-abort");t.setDaemon(true);return t;
        },new ThreadPoolExecutor.AbortPolicy());
    }
    public NativeSdk.Operation sendCancellable(NativeSdk.HttpRequest input,NativeSdk.Callback<NativeSdk.HttpResponse> callback) {
        if(callback==null)throw new IllegalArgumentException("Callback required");
        Request task=new Request(input,callback);
        synchronized(this) {
            if(closed){task.finish(NativeSdk.Result.fail("SDK.DISPOSED","Network provider is closed"));return task;}
            active.add(task);
        }
        try { workers.execute(task); }
        catch(RejectedExecutionException error){task.finish(NativeSdk.Result.fail("NETWORK.BUSY","HTTPS request queue is full or closed"));}
        return task;
    }
    private final class Request implements Runnable,NativeSdk.Operation {
        final NativeSdk.HttpRequest input;
        final AtomicBoolean done=new AtomicBoolean();
        NativeSdk.Callback<NativeSdk.HttpResponse> callback;
        volatile HttpsURLConnection connection;
        Request(NativeSdk.HttpRequest input,NativeSdk.Callback<NativeSdk.HttpResponse> callback){this.input=input;this.callback=callback;}
        public void run() {
            if(done.get())return;
            try {
                URI uri;
                try { uri=new URI(input.url); } catch(Exception error) { throw new Failure("NETWORK.INVALID_REQUEST","Invalid HTTPS URL"); }
                if(!"https".equalsIgnoreCase(uri.getScheme())||uri.getHost()==null||uri.getUserInfo()!=null||uri.getFragment()!=null||
                    !("GET".equals(input.method)||"POST".equals(input.method))||!NativeSdk.validHeaders(input.headers))
                    throw new Failure("NETWORK.INVALID_REQUEST","Valid HTTPS GET/POST and headers required");
                byte[] body=(input.body==null?"":input.body).getBytes(StandardCharsets.UTF_8);
                if(body.length>65536||("GET".equals(input.method)&&body.length!=0))throw new Failure("NETWORK.INVALID_REQUEST","Body exceeds 65536 bytes or was supplied for GET");
                HttpsURLConnection opened=connections.open(uri.toURL());connection=opened;
                if(done.get())return;
                opened.setInstanceFollowRedirects(false);opened.setUseCaches(false);
                opened.setConnectTimeout(timeoutMs);opened.setReadTimeout(timeoutMs);opened.setRequestMethod(input.method);
                opened.setRequestProperty("Accept-Encoding","identity");
                for(Map.Entry<String,String> header:input.headers.entrySet())opened.setRequestProperty(header.getKey(),header.getValue());
                if("POST".equals(input.method)) {
                    opened.setDoOutput(true);opened.setFixedLengthStreamingMode(body.length);
                    if(!input.headers.keySet().stream().anyMatch(k->"content-type".equalsIgnoreCase(k)))opened.setRequestProperty("Content-Type","application/json; charset=utf-8");
                    try(OutputStream output=opened.getOutputStream()){output.write(body);}
                }
                int status=opened.getResponseCode();
                if(status<100||status>599)throw new Failure("NETWORK.INVALID_RESPONSE","Invalid HTTP status");
                if(opened.getContentLengthLong()>maxResponseBytes)throw new Failure("NETWORK.RESPONSE_TOO_LARGE","HTTPS response exceeds configured limit");
                byte[] data;
                try(InputStream stream=status>=400?opened.getErrorStream():opened.getInputStream()) {
                    ByteArrayOutputStream output=new ByteArrayOutputStream();byte[] chunk=new byte[8192];
                    if(stream!=null) for(int n;(n=stream.read(chunk))!=-1;) {
                        if(done.get())return;
                        if(n>maxResponseBytes-output.size())throw new Failure("NETWORK.RESPONSE_TOO_LARGE","HTTPS response exceeds configured limit");
                        output.write(chunk,0,n);
                    }
                    data=output.toByteArray();
                }
                String bodyText=StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(data)).toString();
                finish(NativeSdk.Result.ok(new NativeSdk.HttpResponse(status,bodyText)));
            } catch(Failure error){finish(NativeSdk.Result.fail(error.code,error.getMessage()));}
            catch(CharacterCodingException error){finish(NativeSdk.Result.fail("NETWORK.INVALID_RESPONSE","Response is not UTF-8"));}
            catch(SocketTimeoutException error){finish(NativeSdk.Result.fail("NETWORK.TIMEOUT","HTTPS connection or read timed out"));}
            catch(SSLException error){finish(NativeSdk.Result.fail("NETWORK.TLS_FAILED","TLS verification or negotiation failed"));}
            catch(SecurityException error){finish(NativeSdk.Result.fail("NETWORK.PERMISSION_DENIED","Network permission denied"));}
            catch(Exception error){finish(NativeSdk.Result.fail("NETWORK.TRANSPORT_FAILED","HTTPS transport failed"));}
            finally { disconnect(); }
        }
        void disconnect(){HttpsURLConnection current=connection;connection=null;if(current!=null)try{current.disconnect();}catch(Exception ignored) { }}
        boolean finish(NativeSdk.Result<NativeSdk.HttpResponse> result) {
            if(!done.compareAndSet(false,true))return false;
            workers.remove(this);
            synchronized(HttpsNetwork.this){active.remove(this);}
            NativeSdk.Callback<NativeSdk.HttpResponse> target=callback;callback=null;
            if(target!=null)try{target.complete(result);}catch(Exception ignored) { }
            return true;
        }
        public void cancel(){
            if(!finish(NativeSdk.Result.fail("NETWORK.CANCELLED","Local request cancelled; server action may continue")))return;
            // Some platform implementations block in disconnect while a read holds an internal lock.
            // Never stall the game's UI thread; worker finally/read timeout remains the fallback cleanup.
            HttpsURLConnection current=connection;
            if(current!=null)try{aborts.execute(()->{try{current.disconnect();}catch(Exception ignored){}});}catch(RejectedExecutionException ignored) { }
        }
    }
    private static final class Failure extends IOException {
        private static final long serialVersionUID=1L;
        final String code;
        Failure(String code,String reason){super(reason);this.code=code;}
    }
    public void close() {
        List<Request> pending;
        synchronized(this){if(closed)return;closed=true;pending=new ArrayList<>(active);}
        for(Request request:pending)request.cancel();workers.shutdownNow();aborts.shutdown();
    }
}
