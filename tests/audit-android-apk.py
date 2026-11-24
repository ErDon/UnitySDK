"""Limited, reproducible smoke APK audit; not an exhaustive vendor/resource certification."""
import argparse, hashlib, json, pathlib, zipfile

parser=argparse.ArgumentParser()
parser.add_argument('--apk',default='.tests/android-smoke/SDKSmoke.apk')
parser.add_argument('--mapping',default='.tests/AndroidBridgeProject/Library/Bee/Android/Prj/IL2CPP/Gradle/launcher/build/outputs/mapping/debug/mapping.txt')
parser.add_argument('--output',default='android/validation/development-apk.json')
args=parser.parse_args()
apk=pathlib.Path(args.apk);mapping=pathlib.Path(args.mapping)
text=mapping.read_text(encoding='utf-8')
assert 'com.erdon.sdk.android.UnityBridge -> com.erdon.sdk.android.UnityBridge:' in text, 'JNI class renamed or missing from R8 mapping'
with zipfile.ZipFile(apk) as archive:
    names=archive.namelist();native=[name for name in names if name.startswith('lib/')]
    assert 'lib/arm64-v8a/libil2cpp.so' in names, 'IL2CPP ARM64 library absent'
    assert all(name.startswith('lib/arm64-v8a/') for name in native), 'Unexpected ABI'
    dex=b''.join(archive.read(name) for name in names if name.endswith('.dex'))
    assert b'Lcom/erdon/sdk/android/UnityBridge;' in dex, 'JNI class absent from DEX'
    assert not any(value in dex for value in [b'Lcom/bytedance/',b'Lcom/tiktok/',b'Lcom/tencent/mm/']), 'Known foreign vendor DEX signature'
report={'kind':'Development APK technical validation, not production certification','applicationId':'com.erdon.sdk.smoke',
        'apkSha256':hashlib.sha256(apk.read_bytes()).hexdigest(),
        'aarSha256':json.loads(pathlib.Path('android/build-manifest.json').read_text(encoding='utf-8'))['aarSha256'],
        'architecture':'arm64-v8a','backend':'IL2CPP','r8MappingSha256':hashlib.sha256(mapping.read_bytes()).hexdigest(),
        'nativeLibraries':native,'checks':['JNI class retained in DEX and R8 mapping','Known ByteDance/TikTok/WeChat DEX prefixes absent','Only ARM64 native libraries'],
        'limitations':['Not exhaustive vendor/resource detection','Not production or physical-device validation','Other module combinations not covered']}
output=pathlib.Path(args.output);output.parent.mkdir(parents=True,exist_ok=True)
output.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print('ANDROID_APK_AUDIT_PASS',apk.stat().st_size,'bytes')
