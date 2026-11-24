package com.erdon.sdk;

import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.cert.Certificate;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
import javax.net.ssl.HttpsURLConnection;

public final class HttpsNetworkTests {
    static void check(boolean value,String message){if(!value)throw new AssertionError(message);}
    static class Connection extends HttpsURLConnection {
        int status=200;byte[] body="中文".getBytes(StandardCharsets.UTF_8);boolean disconnected;long length=-1;
        ByteArrayOutputStream output=new ByteArrayOutputStream();
        Connection()throws Exception{super(new URL("https://unit.test/"));}
        public void connect(){}public void disconnect(){disconnected=true;}public boolean usingProxy(){return false;}
        public String getCipherSuite(){return "test-only";}public Certificate[] getLocalCertificates(){return null;}public Certificate[] getServerCertificates(){return new Certificate[0];}
        public int getResponseCode()throws IOException{return status;}public long getContentLengthLong(){return length;}
        public InputStream getInputStream(){return new ByteArrayInputStream(body);}public InputStream getErrorStream(){return getInputStream();}
        public OutputStream getOutputStream(){return output;}
    }
    static NativeSdk.Result<NativeSdk.HttpResponse> request(HttpsNetwork provider,NativeSdk.HttpRequest input)throws Exception {
        CountDownLatch ready=new CountDownLatch(1);AtomicReference<NativeSdk.Result<NativeSdk.HttpResponse>> result=new AtomicReference<>();
        provider.sendCancellable(input,r->{result.set(r);ready.countDown();});check(ready.await(3,TimeUnit.SECONDS),"no response");return result.get();
    }
    static NativeSdk.HttpRequest get(){return new NativeSdk.HttpRequest("https://unit.test/","GET","",null);}
    public static void main(String[] args)throws Exception {
        Connection success=new Connection();
        try(HttpsNetwork provider=new HttpsNetwork(100,64,u->success)){
            NativeSdk.Result<NativeSdk.HttpResponse> result=request(provider,get());
            check(result.success()&&result.value.status==200&&result.value.body.equals("中文"),"UTF-8 success");
            check(!success.getInstanceFollowRedirects()&&!success.getUseCaches()&&success.getConnectTimeout()==100&&success.getReadTimeout()==100,"transport policy");
        }
        Connection post=new Connection();post.status=422;
        try(HttpsNetwork provider=new HttpsNetwork(100,64,u->post)){
            NativeSdk.Result<NativeSdk.HttpResponse> result=request(provider,new NativeSdk.HttpRequest("https://unit.test/","POST","中文",Collections.singletonMap("Authorization","Bearer secret")));
            check(result.success()&&result.value.status==422&&Arrays.equals(post.output.toByteArray(),"中文".getBytes(StandardCharsets.UTF_8)),"POST/non-2xx preservation");
        }
        Connection redirect=new Connection();redirect.status=302;AtomicInteger opens=new AtomicInteger();
        try(HttpsNetwork provider=new HttpsNetwork(100,64,u->{opens.incrementAndGet();return redirect;})){
            check(request(provider,get()).value.status==302&&opens.get()==1&&!redirect.getInstanceFollowRedirects(),"redirect followed");
        }
        Connection large=new Connection();large.body=new byte[65];
        try(HttpsNetwork provider=new HttpsNetwork(100,64,u->large)){check(request(provider,get()).code.equals("NETWORK.RESPONSE_TOO_LARGE"),"streaming limit");}
        Connection invalid=new Connection();invalid.body=new byte[]{(byte)0xff};
        try(HttpsNetwork provider=new HttpsNetwork(100,64,u->invalid)){check(request(provider,get()).code.equals("NETWORK.INVALID_RESPONSE"),"invalid UTF-8");}
        try(HttpsNetwork provider=new HttpsNetwork(100,64,u->{throw new SocketTimeoutException("must not escape");})){
            check(request(provider,get()).code.equals("NETWORK.TIMEOUT"),"timeout mapping");
        }
        try(HttpsNetwork provider=new HttpsNetwork(100,64,u->{throw new javax.net.ssl.SSLHandshakeException("must not escape");})){
            check(request(provider,get()).code.equals("NETWORK.TLS_FAILED"),"TLS mapping");
        }
        AtomicInteger invalidOpens=new AtomicInteger();
        try(HttpsNetwork provider=new HttpsNetwork(100,64,u->{invalidOpens.incrementAndGet();return new ConnectionUnchecked();})){
            check(request(provider,new NativeSdk.HttpRequest("http://unit.test","GET","",null)).code.equals("NETWORK.INVALID_REQUEST"),"cleartext");
            check(request(provider,new NativeSdk.HttpRequest("https://unit.test","GET","body",null)).code.equals("NETWORK.INVALID_REQUEST"),"GET body");
            check(request(provider,new NativeSdk.HttpRequest("https://unit.test","POST",String.join("",Collections.nCopies(30000,"中")),null)).code.equals("NETWORK.INVALID_REQUEST"),"UTF-8 request byte limit");
            check(invalidOpens.get()==0,"invalid input opened socket");
        }
        CountDownLatch started=new CountDownLatch(1),unblock=new CountDownLatch(1),cancelled=new CountDownLatch(1);AtomicInteger callbacks=new AtomicInteger();
        Connection slow=new Connection(){
            public int getResponseCode()throws IOException{started.countDown();try{unblock.await(2,TimeUnit.SECONDS);}catch(InterruptedException e){Thread.currentThread().interrupt();}throw new IOException("closed");}
            public void disconnect(){super.disconnect();unblock.countDown();}
        };
        try(HttpsNetwork provider=new HttpsNetwork(100,64,u->slow)){
            NativeSdk.Operation operation=provider.sendCancellable(get(),r->{callbacks.incrementAndGet();check("NETWORK.CANCELLED".equals(r.code),"cancel code");cancelled.countDown();});
            check(started.await(2,TimeUnit.SECONDS),"request not started");operation.cancel();operation.cancel();
            check(cancelled.await(1,TimeUnit.SECONDS)&&unblock.await(1,TimeUnit.SECONDS)&&slow.disconnected&&callbacks.get()==1,"cancel/disconnect/once");
        }
        AtomicInteger stops=new AtomicInteger();CountDownLatch timeout=new CountDownLatch(1);
        NativeSdk.CancellableNetwork channel=new NativeSdk.CancellableNetwork(){
            public NativeSdk.Operation sendCancellable(NativeSdk.HttpRequest r,NativeSdk.Callback<NativeSdk.HttpResponse> c){return stops::incrementAndGet;}
            public void close(){}
        };
        try(NativeSdk sdk=new NativeSdk(EnumSet.of(NativeSdk.Module.NETWORK),m->channel,Runnable::run,null,s->{},20)){
            sdk.send(get(),r->{check("OPERATION.TIMEOUT".equals(r.code),"core timeout");timeout.countDown();});
            check(timeout.await(2,TimeUnit.SECONDS)&&stops.get()==1,"core timeout did not cancel transport");
        }
        HttpsNetwork closed=new HttpsNetwork();closed.close();check(request(closed,get()).code.equals("SDK.DISPOSED"),"closed transport");
        CountDownLatch busyWorkers=new CountDownLatch(2),blockWorkers=new CountDownLatch(1);AtomicInteger busy=new AtomicInteger(),closedCalls=new AtomicInteger();
        HttpsNetwork queued=new HttpsNetwork(100,64,u->{
            busyWorkers.countDown();try{blockWorkers.await();}catch(InterruptedException error){Thread.currentThread().interrupt();throw new IOException("cancelled");}return new ConnectionUnchecked();
        });
        for(int i=0;i<35;i++)queued.sendCancellable(get(),r->{if("NETWORK.BUSY".equals(r.code))busy.incrementAndGet();if("NETWORK.CANCELLED".equals(r.code))closedCalls.incrementAndGet();});
        check(busyWorkers.await(2,TimeUnit.SECONDS)&&busy.get()==1,"bounded active/queued requests");queued.close();blockWorkers.countDown();
        check(closedCalls.get()==34,"close must cancel active and queued work exactly once");
        System.out.println("ANDROID_HTTPS_TESTS_PASS: POST/UTF-8/status, no redirects, limits, TLS/errors, invalid input, cancellation, timeout transport release");
    }
    static final class ConnectionUnchecked extends HttpsURLConnection {
        ConnectionUnchecked(){super(null);}public void connect(){}public void disconnect(){}public boolean usingProxy(){return false;}
        public String getCipherSuite(){return "test";}public Certificate[] getLocalCertificates(){return null;}public Certificate[] getServerCertificates(){return null;}
    }
}
