"""Optional end-to-end MCP/HTTP smoke tests. Python 3 stdlib, no real Codex login.
Usage: python tests/smoke.py /path/to/dotnet
Run dotnet build -c Release first.
"""
import json, os, pathlib, subprocess, sys, time, urllib.request, urllib.error
root = pathlib.Path(__file__).resolve().parents[1]
dotnet = sys.argv[1] if len(sys.argv)>1 else 'dotnet'
server = root/'src/CodexUsageMcp/bin/Release/net10.0/CodexUsageMcp.dll'
mock = root/'tests/CodexUsageMcp.Tests/bin/Release/net10.0/CodexUsageMcp.Tests.dll'
env = os.environ.copy()
env.update(Codex__Executable=dotnet, Codex__Arguments__0=str(mock), Codex__Arguments__1='--mock', Codex__Arguments__2='ok', Http__Port='15078')
# JSON once mode, from arbitrary cwd, all real account access replaced by mock.
one = subprocess.run([dotnet,str(server),'--once'],env=env,cwd='/tmp' if os.name!='nt' else os.environ['TEMP'],capture_output=True,text=True,timeout=15)
assert one.returncode==0,(one.returncode,one.stderr)
assert json.loads(one.stdout)['buckets'][0]['windows'][0]['remainingPercent']==75
print('PASS: --once JSON with mock, arbitrary cwd')
# MCP stdio JSON-RPC handshake and tool discovery/call.
p = subprocess.Popen([dotnet,str(server),'--stdio'],env=env,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True)
try:
 def rpc(message):
  p.stdin.write(json.dumps(message)+'\n'); p.stdin.flush()
  while True:
   line=p.stdout.readline(); assert line,'stdio closed'
   response=json.loads(line)
   if response.get('id')==message['id']: return response
 r=rpc({'jsonrpc':'2.0','id':1,'method':'initialize','params':{'protocolVersion':'2025-06-18','capabilities':{},'clientInfo':{'name':'smoke','version':'1'}}})
 assert 'result' in r,r
 p.stdin.write(json.dumps({'jsonrpc':'2.0','method':'notifications/initialized'})+'\n');p.stdin.flush()
 r=rpc({'jsonrpc':'2.0','id':2,'method':'tools/list','params':{}})
 assert len(r['result']['tools'])==1 and r['result']['tools'][0]['name']=='get_codex_usage_status',r
 r=rpc({'jsonrpc':'2.0','id':3,'method':'tools/call','params':{'name':'get_codex_usage_status','arguments':{}}})
 assert not r['result'].get('isError'),r
 assert '75' in json.dumps(r),r
 print('PASS: MCP stdio initialize, tools/list, tools/call')
finally: p.terminate();p.wait(timeout=5)
p=subprocess.Popen([dotnet,str(server)],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
try:
 for i in range(100):
  try:
   response=urllib.request.urlopen('http://127.0.0.1:15078/healthz',timeout=1); break
  except (OSError,urllib.error.URLError):time.sleep(.1)
 else: raise AssertionError('HTTP not ready')
 assert json.load(response)['scope']=='process-only'
 for headers in ({'Origin':'http://evil.example'},{'Host':'evil.example'}):
  try: urllib.request.urlopen(urllib.request.Request('http://127.0.0.1:15078/healthz',headers=headers),timeout=2);raise AssertionError('expected 403')
  except urllib.error.HTTPError as e: assert e.code==403
 request=urllib.request.Request('http://127.0.0.1:15078/mcp',data=json.dumps({'jsonrpc':'2.0','id':4,'method':'initialize','params':{'protocolVersion':'2025-06-18','capabilities':{},'clientInfo':{'name':'smoke','version':'1'}}}).encode(),headers={'Content-Type':'application/json','Accept':'application/json, text/event-stream'})
 with urllib.request.urlopen(request,timeout=10) as response: assert response.status==200 and b'result' in response.read()
 print('PASS: HTTP health, Origin/Host rejection, MCP initialize')
finally:p.terminate();p.wait(timeout=5)
env['Kestrel__Endpoints__Unexpected__Url']='http://0.0.0.0:15079'
result=subprocess.run([dotnet,str(server)],env=env,capture_output=True,text=True,timeout=10)
assert result.returncode!=0
print('PASS: extra Kestrel endpoint fails closed')
