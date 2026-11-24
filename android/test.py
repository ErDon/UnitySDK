"""Run the engine-independent Java behavior tests; no Android UI or vendor calls are simulated as real."""
import pathlib, shutil, subprocess
root=pathlib.Path(__file__).resolve().parent
out=root/'build/test-classes'
out.mkdir(parents=True,exist_ok=True)
sources=sorted((root/'core/src').rglob('*.java'))+sorted((root/'tests').glob('*.java'))
subprocess.run([shutil.which('javac'),'-J-Duser.language=en','--release','8','-Xlint:all,-options','-Werror','-encoding','UTF-8','-d',str(out),*map(str,sources)],check=True)
subprocess.run([shutil.which('java'),'-cp',str(out),'NativeSdkTests'],check=True)
