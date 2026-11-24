package com.erdon.sdk;

import com.sun.net.httpserver.*;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
import javax.net.ssl.*;

/** Real loopback TLS/HTTP, not a platform service. Test certificate trust is per connection only. */
public final class HttpsLoopbackTests {
    static void check(boolean value,String message){if(!value)throw new AssertionError(message);}
    static NativeSdk.Result<NativeSdk.HttpResponse> send(HttpsNetwork provider,String url,String method,String body)throws Exception {
        CountDownLatch ready=new CountDownLatch(1);AtomicReference<NativeSdk.Result<NativeSdk.HttpResponse>> result=new AtomicReference<>();
        provider.sendCancellable(new NativeSdk.HttpRequest(url,method,body,null),r->{result.set(r);ready.countDown();});
        check(ready.await(5,TimeUnit.SECONDS),"TLS request did not finish");return result.get();
    }
    static void respond(HttpExchange exchange,int status,String body)throws IOException {
        byte[] data=body.getBytes(StandardCharsets.UTF_8);exchange.sendResponseHeaders(status,data.length);
        try(OutputStream output=exchange.getResponseBody()){output.write(data);}
    }
    public static void main(String[] args)throws Exception {
        KeyStore keys=KeyStore.getInstance("PKCS12");char[] password="sdk-test-password".toCharArray();
        try(InputStream input=new FileInputStream(args[0])){keys.load(input,password);}
        KeyManagerFactory km=KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm());km.init(keys,password);
        TrustManagerFactory tm=TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm());tm.init(keys);
        SSLContext tls=SSLContext.getInstance("TLS");tls.init(km.getKeyManagers(),tm.getTrustManagers(),null);
        HttpsServer server=HttpsServer.create(new InetSocketAddress("127.0.0.1",0),0);
        server.setHttpsConfigurator(new HttpsConfigurator(tls));
        ExecutorService executor=Executors.newCachedThreadPool(r->{Thread t=new Thread(r,"sdk-test-https");t.setDaemon(true);return t;});server.setExecutor(executor);
        AtomicInteger redirectHits=new AtomicInteger();
        CountDownLatch reading=new CountDownLatch(1),releaseRead=new CountDownLatch(1);
        server.createContext("/ok",x->respond(x,200,"中文"));
        server.createContext("/error",x->respond(x,503,"unavailable"));
        server.createContext("/redirect",x->{x.getResponseHeaders().add("Location","/target");respond(x,302,"redirect");});
        server.createContext("/target",x->{redirectHits.incrementAndGet();respond(x,200,"should not follow");});
        server.createContext("/echo",x->{ByteArrayOutputStream body=new ByteArrayOutputStream();byte[] buffer=new byte[1024];for(int n;(n=x.getRequestBody().read(buffer))!=-1;)body.write(buffer,0,n);respond(x,201,new String(body.toByteArray(),StandardCharsets.UTF_8));});
        server.createContext("/large",x->{x.sendResponseHeaders(200,0);try(OutputStream output=x.getResponseBody()){output.write(new byte[65]);}});
        server.createContext("/slow",x->{
            x.sendResponseHeaders(200,0);
            try(OutputStream output=x.getResponseBody()){
                output.write('x');output.flush();reading.countDown();
                try{releaseRead.await(4,TimeUnit.SECONDS);}catch(InterruptedException error){Thread.currentThread().interrupt();}
            }
        });
        server.start();String base="https://localhost:"+server.getAddress().getPort();
        try(HttpsNetwork trusted=new HttpsNetwork(2000,64,url->{HttpsURLConnection c=(HttpsURLConnection)url.openConnection();c.setSSLSocketFactory(tls.getSocketFactory());return c;});
            HttpsNetwork untrusted=new HttpsNetwork(2000,64)) {
            NativeSdk.Result<NativeSdk.HttpResponse> ok=send(trusted,base+"/ok","GET","");check(ok.success()&&ok.value.body.equals("中文"),"real TLS/UTF-8 GET");
            NativeSdk.Result<NativeSdk.HttpResponse> echo=send(trusted,base+"/echo","POST","{\"text\":\"中文\"}");check(echo.success()&&echo.value.status==201&&echo.value.body.contains("中文"),"real UTF-8 POST");
            check(send(trusted,base+"/error","GET","").value.status==503,"non-2xx status lost");
            check(send(trusted,base+"/redirect","GET","").value.status==302&&redirectHits.get()==0,"redirect leaked request");
            check("NETWORK.RESPONSE_TOO_LARGE".equals(send(trusted,base+"/large","GET","").code),"chunked size limit");
            check("NETWORK.TLS_FAILED".equals(send(untrusted,base+"/ok","GET","").code),"untrusted certificate accepted");
            CountDownLatch cancelled=new CountDownLatch(1);
            NativeSdk.Operation operation=trusted.sendCancellable(new NativeSdk.HttpRequest(base+"/slow","GET","",null),r->{if("NETWORK.CANCELLED".equals(r.code))cancelled.countDown();});
            check(reading.await(3,TimeUnit.SECONDS),"slow response not started");
            long start=System.nanoTime();operation.cancel();
            check(TimeUnit.NANOSECONDS.toMillis(System.nanoTime()-start)<1000&&cancelled.await(1,TimeUnit.SECONDS),"cancel blocked caller on socket read");
            releaseRead.countDown();
            System.out.println("ANDROID_REAL_HTTPS_PASS: loopback TLS, GET/POST, UTF-8, non-2xx, no redirect, chunked limit, trust rejection, nonblocking cancellation");
        } finally {releaseRead.countDown();server.stop(0);executor.shutdownNow();}
    }
}
