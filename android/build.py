"""Offline AAR build using the installed JDK and Android SDK (no Gradle download)."""
import argparse, hashlib, json, pathlib, shutil, subprocess, zipfile

root = pathlib.Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('--android-jar', required=True)
parser.add_argument('--javac', default=shutil.which('javac'))
args = parser.parse_args()
output = root / 'build'
classes = output / 'classes'
classes.mkdir(parents=True, exist_ok=True)
# Remove only old generated class files within this fixed build directory.
for stale in classes.rglob('*.class'):
    stale.unlink()
sources = sorted((root/'core/src').rglob('*.java')) + sorted((root/'library/src').rglob('*.java'))
subprocess.run([args.javac, '-J-Duser.language=en', '--release', '8', '-encoding', 'UTF-8', '-Xlint:all,-options', '-Werror',
                '-cp', args.android_jar, '-d', str(classes), *map(str,sources)], check=True)

def archive(path, entries):
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as target:
        for name, data in sorted(entries):
            info = zipfile.ZipInfo(name, (2026,1,1,0,0,0))
            info.compress_type = zipfile.ZIP_DEFLATED
            target.writestr(info, data)

jar=output/'classes.jar'
archive(jar, [(p.relative_to(classes).as_posix(),p.read_bytes()) for p in classes.rglob('*.class')])
aar=output/'erdon-android-0.1.0.aar'
archive(aar, [('classes.jar',jar.read_bytes()), ('AndroidManifest.xml',(root/'library/AndroidManifest.xml').read_bytes()),
              ('proguard.txt',(root/'library/consumer-rules.pro').read_bytes()), ('R.txt',b'')])
plugin=root.parent/'Packages/com.erdon.unitysdk.android/Runtime/Plugins/Android/erdon-android.aar'
plugin.parent.mkdir(parents=True,exist_ok=True)
shutil.copyfile(aar,plugin)
sha=hashlib.sha256(aar.read_bytes()).hexdigest()
manifest={'version':'0.1.0','protocolVersion':1,'minSdk':26,'aarSha256':sha,
          'sources':{p.relative_to(root).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sources},
          'androidApiSha256':hashlib.sha256(pathlib.Path(args.android_jar).read_bytes()).hexdigest()}
(root/'build-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
# Compile the native sample against the same real Android API without shipping it in the AAR.
sample_output=output/'sample-classes';sample_output.mkdir(exist_ok=True)
import os
subprocess.run([args.javac,'-J-Duser.language=en','--release','8','-Xlint:all,-options','-Werror','-encoding','UTF-8',
                '-cp',str(classes)+os.pathsep+args.android_jar,'-d',str(sample_output),
                *map(str,sorted((root/'samples').glob('*.java')))],check=True)
print('AAR_BUILD_PASS',aar,'sha256='+sha,'native sample compiled')
