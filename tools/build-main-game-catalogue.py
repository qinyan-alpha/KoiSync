"""Reproduce the fixed kPlug 3.4 catalogue from the captured native resource metadata.
This tool is offline evidence preparation; the shipped plugin only reads the resulting TSV.
"""
import csv
import re
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / 'artifacts/main-game-kplug-v014/catalogue.tsv'
rows = list(csv.DictReader(EVIDENCE.open(encoding='utf-8-sig'), delimiter='\t'))
labels = {r['key']: r['name'] for r in csv.DictReader((EVIDENCE.parent / 'names-AnimNameMapper_H.tsv').open(encoding='utf-8-sig'), delimiter='\t')}
localized = dict(re.findall(r'mapper\["([^"]+)"\]\s*=\s*"([^"]+)"', Path(r'E:\Galgames\kk\BepInEx\plugins\kPlug\Language\中文_poseNames.txt').read_text(encoding='utf-8-sig')))
anal_available=set((EVIDENCE.parent/'anal-available.txt').read_text(encoding='utf-8-sig').splitlines())
assert len(rows) == 267 and len({r['resource'] for r in rows}) == 267
aliases = ['# native-key\tcomplete-resource-identity']
extras = ['# key\tname\tsource\ttarget\teffector\tcontact\tanal\tcentered-motion\treason\tresource\tlandmark']
feet = {60,61,62,63,75,76,77,80,81,82,83,84,98,105,106,108,110,111,114}
thigh = {70,71,73,85,109}
oral = {67,68,69,78,79,87,90,92,93,94,95,100,101,112,113,115}
boobs = {89,96,104,119}
hands = {66:'LEFTHAND',88:'LEFTHAND',99:'RIGHTHAND',97:'RIGHTHAND',102:'RIGHTHAND',103:'RIGHTHAND',117:'RIGHTHAND',118:'LEFTHAND'}
for r in rows:
    if r['origin'] == 'native':
        aliases.append(r['mode']+'_'+r['id']+'\t'+r['resource'])
        continue
    asset, mode = r['female'], r['mode']
    source,target,effector,contact = 'Male','Female0','Shaft','VAGINAL'
    landmark,centered = 'Anatomical',False
    reason = 'kPlug 3.4 固定资源配对；不继承复用模板的部位'
    if mode == 'sonyu':
        pass
    elif mode == 'houshi' and asset.startswith('khh_f_'):
        n = int(asset.rsplit('_',1)[1])
        if n in feet: contact='BOTH_FEET'
        elif n in thigh: contact='INTERCRURAL'
        elif n in oral: contact='ORAL'
        elif n in boobs: contact='BREASTS'
        elif n in hands: contact=hands[n];centered=n in {97,102,103,117,118}
        elif n in {65,72,74,107}: contact='AUTO';landmark='Buttocks';centered=True
        elif n==86: contact='AUTO';landmark='LeftArmpit';centered=True
        elif n==64: source,target,effector,contact='Female0','Male','Head','ANAL'
        elif n==91: contact='BOTH_FEET'
        else: raise ValueError('Unclassified service '+asset)
    elif mode == 'houshi' and asset.startswith('khe_f_'):
        if asset in {'khe_f_01','khe_f_02','khe_f_11'}: contact='ORAL'
        elif asset in {'khe_f_03','khe_f_04','khe_f_07','khe_f_09','khe_f_10','khe_f_12'}: effector='Head'
        elif asset in {'khe_f_05','khe_f_13'}: effector,contact='Head','ANAL'
        elif asset in {'khe_f_05_00','khe_f_08'}: effector='RightFinger'
        elif asset=='khe_f_06': effector,contact='RightFinger','ANAL'
        else: raise ValueError('Unclassified extra service '+asset)
    elif mode == 'masturbation':
        source=target='Female0'
        n=int(asset.rsplit('_',1)[1])
        if n in {4,5,6,7}: effector='LeftFinger' if n==5 else 'RightFinger';contact='ANAL' if n in {5,6} else 'VAGINAL'
        elif n in {8,9}:
            effector='AnimatedProp';reason+='；读取 p_item_dildo 的独立动画器及其渲染器拥有的六段骨链'
        elif n==10:
            effector='SceneAnchor';reason+='；振动物件无可读平移轴，记录骨盆相对场景的实际位移'
        else: raise ValueError('Unclassified solo '+asset)
    elif mode == 'lesbian':
        effector='Head'
        source,target=('Female0','Female1') if asset in {'khr_f_05_01','khr_f_06_01'} else ('Female1','Female0')
    elif mode in {'houshi3P','sonyu3P'}:
        target='Female1' if int(r['id'])%2 else 'Female0'
        contact='ORAL' if mode=='houshi3P' else 'VAGINAL'
    else: raise ValueError('Unclassified mode '+mode)
    # External service surfaces have no anatomical insertion plane. Use a fixed
    # first-pose ruler, then preserve their actual relative movement.
    if mode == 'houshi' and effector == 'Shaft' and contact != 'ORAL': centered=True
    assert (r['anal']=='True') == (labels[asset] in anal_available), 'Anal branch differs from kPlug explicit availability: '+asset
    key='kplug_'+asset
    label='kPlug · '+localized.get(labels[asset], labels[asset])
    extras.append('\t'.join([key,label,source,target,effector,contact,r['anal'].lower(),str(centered).lower(),reason,r['resource'],landmark]))
(ROOT/'src/KoiSync.MainGame/NativeResources.tsv').write_text('\n'.join(aliases)+'\n',encoding='utf-8')
(ROOT/'src/KoiSync.MainGame/KPlugActions.tsv').write_text('\n'.join(extras)+'\n',encoding='utf-8')
print(f'Wrote {len(aliases)-1} native aliases and {len(extras)-1} fixed kPlug definitions')
