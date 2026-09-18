import subprocess,xml.etree.ElementTree as ET,re,time,sys
sys.stdout.reconfigure(encoding='utf-8')
adb=r'C:/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe'
def run(*args): return subprocess.run([adb,*args],capture_output=True,text=True,encoding='utf-8').stdout
def nodes():
 for attempt in range(3):
  result=run('shell','uiautomator','dump','/sdcard/settings-probe.xml')
  if 'dumped to' in result:
   return list(ET.fromstring(run('shell','cat','/sdcard/settings-probe.xml')).iter('node'))
  time.sleep(.3)
 return []
def bounds(n): return list(map(int,re.findall(r'\d+',n.attrib['bounds'])))
focus=run('shell','dumpsys','window')
if not any(('mCurrentFocus' in line or 'mFocusedApp' in line) and 'com.companyname.openiptv.settingsvalidation' in line for line in focus.splitlines()):
 print('Test app is not foreground; no action taken.');sys.exit(1)
mode=sys.argv[1]
if mode=='texts':
 for n in nodes():
  text=n.get('text') or n.get('content-desc')
  if text:print(text,n.get('bounds'))
  elif n.get('checkable')=='true':print('TOGGLE',n.get('checked'),n.get('bounds'))
elif mode=='tap':
 target=sys.argv[2]
 for step in range(5 if '--scroll' in sys.argv else 1):
  ns=nodes()
  match=next((n for n in ns if n.get('text')==target or n.get('content-desc')==target),None)
  if match is not None:
   x1,y1,x2,y2=bounds(match)
   if x2>x1 and y2>y1:
    print('Tapped',target,match.get('bounds'));run('shell','input','tap',str((x1+x2)//2),str((y1+y2)//2));break
  scroll=next((n for n in ns if n.get('scrollable')=='true'),None)
  if scroll is None:print('Not found:',target);break
  x1,y1,x2,y2=bounds(scroll);x=(x1+x2)//2;run('shell','input','swipe',str(x),str(y2-100),str(x),str(y1+150),'350');time.sleep(.3)
 else:print('Not found yet:',target)
