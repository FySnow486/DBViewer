"""Run an isolated local server and verify persistence across a real restart."""
import json, os, pathlib, subprocess, tempfile, time, urllib.request, urllib.error
ROOT=pathlib.Path(__file__).resolve().parents[1]
BASE='http://127.0.0.1:5083'
process=None

def call(path,body=None,method=None,origin=None):
    headers={'Content-Type':'application/json'}
    if origin: headers['Origin']=origin
    req=urllib.request.Request(BASE+path,data=json.dumps(body).encode() if body is not None else None,headers=headers,method=method)
    try:
        with urllib.request.urlopen(req,timeout=10) as r:
            payload=r.read()
            return r.status,json.loads(payload) if payload else None
    except urllib.error.HTTPError as e:
        payload=e.read()
        return e.code,json.loads(payload) if payload else None

def start(directory):
    global process
    env=dict(os.environ,DBVIEWER_DATA_DIR=directory,ASPNETCORE_URLS=BASE)
    process=subprocess.Popen(['dotnet',str(ROOT/'bin/Debug/net8.0/DBViewer.dll')],cwd=ROOT,env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    for _ in range(80):
        if process.poll() is not None: raise RuntimeError('isolated test server exited')
        try:
            if call('/api/connections')[0]==200:return
        except OSError:pass
        time.sleep(.1)
    raise RuntimeError('server not ready')

def stop():
    global process
    if process:
        process.terminate()
        try:process.wait(timeout=10)
        except subprocess.TimeoutExpired:process.kill();process.wait()
        process=None

try:
    with tempfile.TemporaryDirectory(prefix='dbviewer-persistence-') as directory:
        start(directory)
        profile={'name':'Test profile','provider':'sqlserver','connectionString':'Server=localhost,1433;Database=test;User ID=test;Password=TEST_ONLY_DUMMY_PASSWORD;'}
        status,saved=call('/api/connections',profile)
        assert status==200
        id=saved['id']
        state=call('/api/connections')[1]
        assert state['activeId']==id and len(state['items'])==1
        assert 'connectionString' not in state['items'][0]
        file=pathlib.Path(directory,'connections.json')
        assert 'TEST_ONLY_DUMMY_PASSWORD' not in file.read_text()
        assert call('/api/connections/'+id)[1]['connectionString']==profile['connectionString']
        stop();start(directory)
        assert call('/api/connections')[1]['activeId']==id
        assert call('/api/connections/'+id)[1]['connectionString']==profile['connectionString']
        profile.update(id=id,name='Edited profile')
        assert call('/api/connections',profile)[0]==200
        assert len(call('/api/connections')[1]['items'])==1
        assert call('/api/connections/'+id)[1]['name']=='Edited profile'
        assert call('/api/connections/'+id+'/activate',method='POST')[0]==204
        assert call('/api/connections/'+id,method='DELETE',origin='https://untrusted.example')[0]==403
        assert call('/api/connections',{'name':'','provider':'bad','connectionString':'secret'})[0]==400
        assert call('/api/connections/'+id,method='DELETE')[0]==204
        assert call('/api/connections/'+id)[0]==404
        assert call('/api/connections')[1]=={'activeId':None,'items':[]}
        print('PASS: encrypted at rest, redacted list, restart and key persistence, update without duplicate, activation, deletion, validation, origin guard')
finally:stop()
