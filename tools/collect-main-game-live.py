"""Collect final-frame observations while the opt-in native batch plays normally."""
import json
from pathlib import Path
import time
import xml.etree.ElementTree as ET
import argparse

parser=argparse.ArgumentParser()
parser.add_argument('--group',default='pair')
parser.add_argument('--seconds',type=float,default=3600)
args=parser.parse_args()

out=Path(__file__).resolve().parents[1]/'artifacts/main-game-v010'
started=time.time()
stats={}
next_print=0
while True:
    try:
        root=ET.parse(out/'live.xml').getroot()
        n=root.attrib
        if n.get('binding')=='True' and n.get('action'):
            key=n['action']+'/'+n['stage']
            if key not in stats:
                stats[key]={'first':n,'samples':0,'minimum':[1]*6,'maximum':[0]*6,'maxEngagement':0}
            row=stats[key]
            row['samples']+=1
            for i in range(6):
                v=float(n['axis'+str(i)])
                row['minimum'][i]=min(row['minimum'][i],v)
                row['maximum'][i]=max(row['maximum'][i],v)
            row['maxEngagement']=max(row['maxEngagement'],float(n.get('engagement',0)))
            row['last']=n
            for candidate in root.findall('candidate'):
                c=candidate.attrib;d=float(c['distance']);a=float(c['axial'])
                ranges=row.setdefault('candidates',{}).setdefault(c['kind'],{'minimumDistance':d,'maximumDistance':d,'minimumAxial':a,'maximumAxial':a})
                ranges['minimumDistance']=min(ranges['minimumDistance'],d);ranges['maximumDistance']=max(ranges['maximumDistance'],d)
                ranges['minimumAxial']=min(ranges['minimumAxial'],a);ranges['maximumAxial']=max(ranges['maximumAxial'],a)
        if time.monotonic()>next_print:
            (out/('observations-'+args.group+'.json')).write_text(json.dumps(stats,ensure_ascii=False,indent=2),encoding='utf-8')
            print('Observed',len({k.split('/')[0] for k in stats}),'actions /',len(stats),'stages',flush=True)
            next_print=time.monotonic()+30
        done=out/('batch-'+args.group+'-complete.txt')
        if done.exists() and done.stat().st_mtime>=started-1:
            break
        if time.time()-started>=args.seconds:
            break
    except (OSError,ET.ParseError,ValueError):
        pass
    time.sleep(.25)
(out/('observations-'+args.group+'.json')).write_text(json.dumps(stats,ensure_ascii=False,indent=2),encoding='utf-8')
