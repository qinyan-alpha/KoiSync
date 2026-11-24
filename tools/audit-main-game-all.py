"""Audit fixed native and installed kPlug playback; export only observed complete takes."""
import argparse
import csv
from datetime import datetime, timezone
import importlib.util
import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('native_audit', Path(__file__).with_name('audit-main-game-recordings.py'))
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)


def definitions():
    result = {}
    aliases = {}
    for line in (ROOT / 'src/KoiSync.MainGame/NativeResources.tsv').read_text(encoding='utf-8-sig').splitlines():
        if line and not line.startswith('#'):
            key, resource = line.split('\t')
            aliases[key] = resource
    for filename in ['Actions.tsv', 'KPlugActions.tsv']:
        lines = (ROOT / 'src/KoiSync.MainGame' / filename).read_text(encoding='utf-8-sig').splitlines()
        for c in csv.reader((line for line in lines if line and not line.startswith('#')), delimiter='\t'):
            row = {'key': c[0], 'name': c[1], 'source': c[2], 'target': c[3], 'effector': c[4],
                   'contact': c[5], 'analAllowed': c[6] == 'true', 'resource': c[9] if filename == 'KPlugActions.tsv' else aliases[c[0]],
                   'landmark': c[10] if filename == 'KPlugActions.tsv' else 'Anatomical',
                   'weakSource': c[9] if filename == 'Actions.tsv' and len(c) > 9 else None}
            result[c[0]] = row
            if row['analAllowed']:
                result[c[0] + '_anal'] = dict(row, key=c[0] + '_anal', contact='ANAL')
    return result


def expected_stages(key):
    return None if key.startswith(('aibu_', 'peeping_')) else ['A_WLoop', 'A_SLoop'] if key.endswith('_anal') else ['WLoop', 'SLoop']


def audit_group(case, group, solver, defs, selected, groups, issues):
    paths = [case / ('batch-' + group + suffix) for suffix in ['-started.txt', '-complete.txt', '.tsv']]
    label = case.name + '/' + group
    if not all(p.exists() for p in paths) or native.utc_file(paths[1]) < native.utc_file(paths[0]):
        groups[label] = {'complete': False}
        return
    since, until = native.utc_file(paths[0]), native.utc_file(paths[1])
    rows = list(csv.DictReader(paths[2].open(encoding='utf-8-sig'), delimiter='\t'))
    accepted = 0
    errors = []
    for row in rows:
        key = row['action'] + ('_anal' if group in ['anal', 'kplug-anal'] else '')
        d = defs.get(key)
        if d is None:
            errors.append('Unlisted action ' + key)
            continue
        stage = row['stage']
        requested = row.get('requested', '')
        if row['bound'] != 'True' or int(row['samples']) < 10 or not requested or (requested not in ['native-item', 'native-playback'] and stage != requested):
            errors.append(key + '/' + stage + ': native playback did not maintain its requested phase and binding')
            continue
        if solver == 'main-0.1.5':
            if row.get('resource') != d['resource']:
                errors.append(key + ': actual native resource differs from the fixed action')
                continue
            if key.startswith('kplug_') and row.get('kplugCurrent') != d['resource'].split('|')[4]:
                errors.append(key + ': kPlug current animation differs from the selected resource')
                continue
        folder = case / 'runtime/UserData/KoiSyncMainGame/Actions' / key / 'Recordings' / stage
        found = None
        for path in sorted(folder.glob('*.take.xml'), reverse=True):
            if not since - 1 <= path.stat().st_mtime <= until + 1:
                continue
            meta = ET.parse(path).getroot().attrib
            if meta.get('solver') != solver or meta.get('complete') != 'true':
                continue
            try:
                take = native.check_take(path, solver=solver)
                if take['action'] != key or take['stage'] != stage:
                    raise ValueError('Action or stage differs from recorded metadata')
                if solver == 'main-0.1.5' and take['resource'] != d['resource']:
                    raise ValueError('Recording resource differs from actual selected animation')
                pair = take['pair'].split(':')
                source_slot = d['weakSource'] if stage == 'WLoop' and d['weakSource'] else d['source']
                if pair[2:5] != [source_slot, d['target'], d['effector']]:
                    raise ValueError('Recorded participant slots or effector differ from fixed classification')
                found = take
                break
            except (ValueError, KeyError, OSError, ET.ParseError) as ex:
                issues.append({'metadata': str(path), 'reason': str(ex)})
        if found is None:
            errors.append(key + '/' + stage + ': no complete valid take in this playback interval')
            continue
        selected[key + '/' + stage] = found
        accepted += 1
    groups[label] = {'complete': True, 'rows': len(rows), 'accepted': accepted, 'errors': errors,
                     'startedUtc': datetime.fromtimestamp(since, timezone.utc).isoformat(),
                     'completedUtc': datetime.fromtimestamp(until, timezone.utc).isoformat()}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--native', type=Path, default=ROOT / 'artifacts/main-game-v010')
    parser.add_argument('--kplug', type=Path, default=ROOT / 'artifacts/main-game-kplug-v014')
    parser.add_argument('--extra', type=Path, default=ROOT / 'artifacts/main-game-kplug-extra-v015')
    parser.add_argument('--report', type=Path, default=ROOT / 'artifacts/main-game-kplug-v014/validation-all.json')
    parser.add_argument('--export', type=Path)
    parser.add_argument('--strict', action='store_true')
    args = parser.parse_args()
    defs = definitions()
    selected, groups, issues = {}, {}, []
    for group in ['pair', 'solo', 'peeping', 'lesbian', 'fmf', 'mmf', 'anal', 'contact']:
        solver_file = args.native / ('batch-' + group + '-solver.txt')
        solver = solver_file.read_text(encoding='utf-8-sig').strip() if solver_file.exists() else 'main-0.1.4'
        audit_group(args.native, group, solver, defs, selected, groups, issues)
    for group in ['aibu', 'kplug-houshi', 'kplug-sonyu', 'kplug-anal']:
        audit_group(args.kplug, group, 'main-0.1.5', defs, selected, groups, issues)
    for group in ['kplug-solo', 'kplug-lesbian', 'kplug-fmf']:
        case = args.extra if (args.extra / ('batch-' + group + '-started.txt')).exists() else args.kplug
        audit_group(case, group, 'main-0.1.5', defs, selected, groups, issues)
    prop_checks = []
    for key in ['kplug_kht_f_08', 'kplug_kht_f_09']:
        for stage in ['WLoop', 'SLoop']:
            path = args.extra / ('prop-check-' + key + '-' + stage + '.xml')
            if not path.exists():
                issues.append({'metadata': str(path), 'reason': 'Missing native skinned-mesh comparison'})
                continue
            meta = ET.parse(path).getroot().attrib
            check = {'action': key, 'stage': stage, 'frames': int(meta['frames']), 'maxWorldErrorMetres': float(meta['maxWorldErrorMetres']), 'reference': meta['reference']}
            if check['frames'] < 20 or check['maxWorldErrorMetres'] > .0001:
                issues.append({'metadata': str(path), 'reason': 'Prop geometry differs from native final skinned mesh'})
            prop_checks.append(check)
    observed = {t['action'] for t in selected.values()}
    missing = []
    for key in defs:
        stages = expected_stages(key)
        if stages is None:
            if key not in observed:
                missing.append(key)
        else:
            missing.extend(key + '/' + stage for stage in stages if key + '/' + stage not in selected)
    report = {'version': '0.1.5', 'createdUtc': datetime.now(timezone.utc).isoformat(),
              'nativeActions': 96, 'kPlugActions': 171, 'analVariants': 85, 'configuredActions': len(defs),
              'recordedActions': len(observed), 'completeTakes': len(selected), 'axisFiles': len(selected) * 6,
              'recordedFrames': sum(t['frames'] for t in selected.values()), 'sourceSolvers': {},
              'missingRecordings': missing, 'invalidTakes': issues, 'groups': groups,
              'propSkinningChecks': prop_checks,
              'quantizedStationary': [k for k, t in selected.items() if all(lo == hi for lo, hi in t['axisRanges'])],
              'hardwareConnected': False, 'deviceValidation': 'not performed',
              'fixture': 'Isolated native game and installed kPlug 3.4. Native selection, pose initialization and normal animation playback. Final mapped axes before transport delay. No fixture calibration imported into player profiles.',
              'takes': list(selected.values())}
    for take in selected.values():
        report['sourceSolvers'][take['solver']] = report['sourceSolvers'].get(take['solver'], 0) + 1
    report['passed'] = not missing and not issues and all(g.get('complete') and not g.get('errors') for g in groups.values())
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    if args.export:
        args.export.mkdir(parents=True, exist_ok=True)
        for take in selected.values():
            dest = args.export / 'Actions' / take['action'] / 'Recordings' / take['stage']
            dest.mkdir(parents=True, exist_ok=True)
            for name in take['files']:
                source = Path(name)
                target = dest / ('reference-' + source.name)
                if source.name.endswith('.take.xml'):
                    doc = ET.parse(source)
                    doc.getroot().set('reference', 'native-default-actors')
                    doc.write(target, encoding='utf-8')
                else:
                    shutil.copy2(source, target)
            (dest / 'latest.txt').write_text('reference-' + Path(take['metadata']).name.replace('.take.xml', '.funscript'), encoding='utf-8')
        shutil.copy2(args.report, args.export / 'validation.json')
    print(json.dumps({k: report[k] for k in ['passed', 'configuredActions', 'recordedActions', 'completeTakes', 'axisFiles', 'recordedFrames', 'sourceSolvers', 'quantizedStationary']}, ensure_ascii=False, indent=2))
    print('Missing stages:', len(missing), 'Group errors:', sum(len(g.get('errors', [])) for g in groups.values()))
    if args.strict and not report['passed']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
