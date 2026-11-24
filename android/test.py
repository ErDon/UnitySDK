"""Run the engine-independent Java behavior tests; no Android UI or vendor calls are simulated as real."""
import pathlib, shutil, subprocess, tempfile
root=pathlib.Path(__file__).resolve().parent
out=root/'build/test-classes'
out.mkdir(parents=True,exist_ok=True)
sources=sorted((root/'core/src').rglob('*.java'))+sorted((root/'tests').glob('*.java'))
subprocess.run([shutil.which('javac'),'-J-Duser.language=en','--release','8','-Xlint:all,-options','-Werror','-encoding','UTF-8','-d',str(out),*map(str,sources)],check=True)
subprocess.run([shutil.which('java'),'-cp',str(out),'NativeSdkTests'],check=True)
subprocess.run([shutil.which('java'),'-cp',str(out),'com.erdon.sdk.HttpsNetworkTests'],check=True)
with tempfile.TemporaryDirectory(prefix='tls-',dir=root/'build') as temporary:
    cert=pathlib.Path(temporary)/'loopback.p12'
    subprocess.run([shutil.which('keytool'),'-genkeypair','-alias','sdk-test','-keystore',str(cert),
                    '-storetype','PKCS12','-storepass','sdk-test-password','-keypass','sdk-test-password',
                    '-dname','CN=localhost','-ext','SAN=dns:localhost,ip:127.0.0.1','-validity','1',
                    '-keyalg','RSA','-keysize','2048'],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.PIPE)
    subprocess.run([shutil.which('java'),'-cp',str(out),'com.erdon.sdk.HttpsLoopbackTests',str(cert)],check=True)
