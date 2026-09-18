from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse,parse_qs
from pathlib import Path
import json,time
class Handler(BaseHTTPRequestHandler):
 def log_message(self,*args): pass
 def do_GET(self):
  parsed=urlparse(self.path); action=parse_qs(parsed.query).get('action',[''])[0]
  if parsed.path.startswith(('/live/','/movie/','/series/')):
   body=Path('artifacts/ui-validation/fixture.mp4').read_bytes();total=len(body)
   import re
   match=re.match(r'bytes=(\d+)-(\d*)',self.headers.get('Range',''))
   if match:
    start=int(match[1]);end=min(int(match[2]) if match[2] else total-1,total-1)
    self.send_response(206);self.send_header('Content-Range',f'bytes {start}-{end}/{total}');body=body[start:end+1]
   else:self.send_response(200)
   self.send_header('Content-Type','video/mp4');self.send_header('Accept-Ranges','bytes');self.send_header('Content-Length',str(len(body)));self.end_headers()
   try:self.wfile.write(body)
   except (BrokenPipeError,ConnectionResetError):pass
   return
  now=int(time.time())
  data= {'user_info':{'auth':1,'status':'Active'}}
  if action.endswith('_categories'): data=[{'category_id':'1','category_name':'Desporto'},{'category_id':'2','category_name':'Noticias'}]
  elif action=='get_live_streams': data=[{'stream_id':1,'name':'Canal Teste','category_id':'1','epg_channel_id':'test','container_extension':'mp4'},{'stream_id':2,'name':'Noticias Teste','category_id':'2','epg_channel_id':'test','container_extension':'mp4'}]
  elif action=='get_vod_streams':data=[{'stream_id':3,'name':'Filme Teste','category_id':'1','container_extension':'mp4'}]
  elif action=='get_series':data=[{'series_id':4,'name':'Serie Teste','category_id':'1'}]
  elif action=='get_series_info':data={'episodes':{'1':[{'id':5,'episode_num':1,'title':'Episodio 1','container_extension':'mp4'},{'id':6,'episode_num':2,'title':'Episodio 2','container_extension':'mp4'}]}}
  elif action=='get_short_epg':data={'epg_listings':[{'title':'VGVzdGU=','description':'VGVzdGU=','start_timestamp':now-600,'stop_timestamp':now+3600}]}
  body=json.dumps(data).encode();self.send_response(200);self.send_header('Content-Type','application/json');self.send_header('Content-Length',str(len(body)));self.end_headers();self.wfile.write(body)
ThreadingHTTPServer(('127.0.0.1',18765),Handler).serve_forever()
