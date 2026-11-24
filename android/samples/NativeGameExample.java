package com.erdon.sdk.samples;

import android.app.Activity;
import android.os.Bundle;
import android.util.Log;
import com.erdon.sdk.NativeSdk;
import com.erdon.sdk.android.AndroidSdk;
import java.util.EnumSet;

/** Copy into a native app; this sample is not packaged into the library AAR. */
public class NativeGameExample extends Activity {
    private NativeSdk sdk;
    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        sdk=AndroidSdk.create(this,"replace-game-id","test",
            EnumSet.of(NativeSdk.Module.STORAGE,NativeSdk.Module.SHARE),null,line->Log.i("GameSDK",line));
        NativeSdk.Result<String> saved=sdk.setStorage("language","zh");
        if(!saved.success()) Log.w("GameSDK",saved.code);
    }
    /** Connect to a user action instead of launching the chooser during startup. */
    public void share() {
        sdk.share("My Game","https://example.com/game",result->{
            if(!result.success())Log.w("GameSDK",result.code);
        });
    }
    @Override protected void onDestroy() {
        if(sdk!=null){sdk.close();sdk=null;}
        super.onDestroy();
    }
}
