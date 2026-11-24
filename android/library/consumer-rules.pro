# JNI callback and entry names are protocol surface; R8 must not rename/remove them.
-keep class com.erdon.sdk.android.UnityBridge { public *; }
-keep interface com.erdon.sdk.android.UnityBridge$Callback { *; }
-keep interface com.erdon.sdk.android.UnityBridge$Logger { *; }
-keep interface com.erdon.sdk.android.AndroidSdk$ChannelFactory { *; }
