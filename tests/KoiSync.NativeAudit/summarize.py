from pathlib import Path
import csv, json, collections, configparser, math

root = Path(__file__).resolve().parents[2]
out = root / 'artifacts/native-audit-20260913'
def rows(name):
    with (out / name).open(encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f, delimiter='\t'))
catalog, summaries, states, runtime = [rows(n) for n in ('catalog.tsv','skeletal-summary.tsv','states.tsv','mapping-runtime.tsv')]
def key(r): return (r['mode'],r['id'])
def num(r,k): return float(r[k])
mapping = {}
with (root / 'src/Data/NativeAnimations.csv').open(encoding='utf-8-sig') as f:
    for line in f:
        name, sep, profile = line.strip().rpartition(',')
        if sep: mapping[name] = profile
male = [r for r in catalog if r['maleAsset']]
unmapped = [r for r in male if r['name'] not in mapping]
anal = [r for r in catalog if r['isAnal']=='True']
by_key = {key(r):r for r in catalog}
loops = [r for r in summaries if (r['state'] in ('WLoop','SLoop','MLoop') or r['state'] in ('A_WLoop','A_SLoop','A_MLoop') and by_key[key(r)]['isAnal']=='True') and r['error']=='']
sat = [r for r in loops if num(r,'clampL0') >= .95 and num(r,'targetMove') > .005]
hand = [r for r in loops if r['profile'] in ('RIGHTHAND','LEFTHAND') and r['nearestTarget'] in ('RIGHTHAND','LEFTHAND') and r['profile'] != r['nearestTarget']]
config = configparser.ConfigParser(interpolation=None)
config.read(r'E:\Galgames\kk\BepInEx\config\local.koisync.cfg',encoding='utf-8-sig')
gains = dict(config['Native L0'])
gain_ranges = {k: {'gain':float(v),'unsaturated_input_min':.5-.5/float(v),'unsaturated_input_max':.5+.5/float(v)} for k,v in gains.items()}
data = {
    'catalog_count':len(catalog),'mode_counts':dict(collections.Counter(r['mode'] for r in catalog)),
    'male_animation_count':len(male),'mapped_male_count':len(male)-len(unmapped),'unmapped':unmapped,
    'anal_branches':anal,'state_rows':len(states),'sampled_state_rows':len(summaries),
    'sampled_frames':sum(int(r['frames']) for r in summaries),'sampling_errors':[r for r in summaries if r['error']],
    'loop_rows':len(loops),'saturated_loop_rows':len(sat),'saturated_animations':len(set(map(key,sat))),
    'max_r0_step':max(summaries,key=lambda r:num(r,'maxR0Step')),
    'r0_steps_gt_30':[r for r in loops if num(r,'maxR0Step')>30],
    'hand_reference_suspects':hand,
    'configured_global_target':config.get('Native','TargetOverride'),
    'global_override_changes_mapped_targets':sum(mapping.get(r['name'], 'VAGINAL') not in ('VAGINAL','VAGINALSWAP') for r in male if r['name'] in mapping),
    'gain_ranges':gain_ranges,
    'swap_state_mismatches':[r for r in states if r['profile'].endswith('SWAP') and r['femaleHasState']=='True' and r['secondHasState']=='False'],
}
length_groups=collections.defaultdict(list)
with (out/'samples.tsv').open(encoding='utf-8-sig',newline='') as f:
    for r in csv.DictReader(f,delimiter='\t'):
        if r['state'] in ('WLoop','SLoop'):
            length=float(r['length']); distance=float(r['L0'])*length
            length_groups[(r['mode'],r['id'],r['name'],r['state'])].append((length,distance,float(r['L0'])))
def correlation(a,b):
    ma,mb=sum(a)/len(a),sum(b)/len(b)
    denom=math.sqrt(sum((v-ma)**2 for v in a)*sum((v-mb)**2 for v in b))
    return sum((v-ma)*(w-mb) for v,w in zip(a,b))/denom if denom>1e-12 else 0
length_results=[]
for (mode,ident,name,state), samples in length_groups.items():
    length,distance,stroke=map(list,zip(*samples))
    length_results.append({'mode':mode,'id':ident,'name':name,'state':state,'length_min':min(length),'length_max':max(length),'distance_min':min(distance),'distance_max':max(distance),'l0_span_999':(max(stroke)-min(stroke))*999,'length_distance_correlation':correlation(length,distance)})
with (out/'reference-length-variation.csv').open('w',encoding='utf-8-sig',newline='') as f:
    w=csv.DictWriter(f,fieldnames=length_results[0].keys());w.writeheader();w.writerows(length_results)
data['length_examples']=[r for r in length_results if r['name'] in ('正常位','開脚正常位','後背位','騎乗位')]
(out / 'audit-summary.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
for name, selected in [('unmapped-animations.csv',unmapped),('saturated-loops.csv',sat),('hand-reference-suspects.csv',hand)]:
    with (out / name).open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=(selected or catalog)[0].keys());writer.writeheader();writer.writerows(selected)
print(json.dumps({k:v for k,v in data.items() if k not in ('unmapped','anal_branches','hand_reference_suspects','gain_ranges','r0_steps_gt_30','swap_state_mismatches')},ensure_ascii=False,indent=2))
print('HAND SUSPECTS',[(r['name'],r['state'],r['profile'],r['nearestTarget'],r['minL0'],r['maxL0']) for r in hand])
print('R0 JUMPS',[(r['name'],r['state'],r['maxR0Step']) for r in data['r0_steps_gt_30']])
print('SWAP STATE MISMATCH',len(data['swap_state_mismatches']))
