"""Validate native playback evidence and export only complete current-solver takes."""
import argparse
import csv
from datetime import datetime, timezone
import json
import math
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET

PROJECT = Path(__file__).resolve().parents[1]
SOLVER = 'main-0.1.4'
SUFFIXES = ['', '.surge', '.sway', '.twist', '.roll', '.pitch']
GROUPS = ['pair', 'aibu', 'solo', 'peeping', 'lesbian', 'fmf', 'mmf', 'anal', 'contact']


def utc_file(path):
    return datetime.fromisoformat(path.read_text(encoding='utf-8-sig').strip().replace('Z', '+00:00')).timestamp()


def check_take(path, allow_partial=False, solver=SOLVER):
    meta = ET.parse(path).getroot().attrib
    complete = meta.get('complete') == 'true'
    if meta.get('solver') != solver or not complete and not allow_partial:
        raise ValueError('wrong solver or incomplete take')
    duration, start, end, gap = [float(meta[k]) for k in ['duration', 'start', 'end', 'maxGap']]
    if not all(math.isfinite(v) for v in [duration, start, end, gap]) or duration <= 0:
        raise ValueError('invalid animation clock')
    if start > max(.025, duration * .025) + .001 or complete and duration - end > max(.1, duration * .05) + .001:
        raise ValueError('incomplete boundary coverage')
    if gap > max(.15, duration * .1) + .001:
        raise ValueError('excessive sample gap')
    if meta.get('source') != 'final-values-before-transport-delay':
        raise ValueError('unexpected output source')
    stem = str(path)[:-len('.take.xml')]
    times = None
    ranges = []
    files = [path]
    for suffix in SUFFIXES:
        script = Path(stem + suffix + '.funscript')
        doc = json.loads(script.read_text(encoding='utf-8-sig'))
        rows = doc['actions']
        ts = [r['at'] for r in rows]
        values = [r['pos'] for r in rows]
        if len(rows) != int(meta['frames']) or len(rows) < 2:
            raise ValueError('frame count differs from metadata')
        if any(type(t) is not int or t < 0 for t in ts) or any(b <= a for a, b in zip(ts, ts[1:])):
            raise ValueError('timestamps must strictly increase')
        if times is not None and ts != times:
            raise ValueError('six-axis timestamps differ')
        if any(type(v) is not int or not 0 <= v <= 100 for v in values):
            raise ValueError('axis outside finite integer range 0..100')
        if abs(ts[0] / 1000 - start) > .001 or abs(ts[-1] / 1000 - end) > .001:
            raise ValueError('file boundaries differ from metadata')
        times = ts
        ranges.append([min(values), max(values)])
        files.append(script)
    return {'action': meta['action'], 'stage': meta['stage'], 'solver': meta['solver'], 'resource': meta.get('resource', ''), 'complete': complete, 'reason': meta['reason'], 'frames': len(times),
            'duration': duration, 'start': start, 'end': end, 'maxGap': gap,
            'effector': meta['effector'], 'mapping': meta['mapping'], 'pair': meta['pair'],
            'axisRanges': ranges, 'metadata': str(path), 'files': [str(p) for p in files]}


def expected_group(key):
    if key.endswith('_anal'):
        return 'anal'
    mode = key.rsplit('_', 1)[0]
    if mode in ['houshi', 'sonyu']:
        return 'pair'
    if mode == 'masturbation':
        return 'solo'
    if mode in ['houshi3P', 'sonyu3P']:
        return 'fmf'
    if mode in ['houshi3PMMF', 'sonyu3PMMF']:
        return 'mmf'
    return mode


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--out', type=Path, default=PROJECT / 'artifacts/main-game-v010')
    parser.add_argument('--export', type=Path)
    parser.add_argument('--strict', action='store_true')
    args = parser.parse_args()
    definitions = {}
    with (PROJECT / 'src/KoiSync.MainGame/Actions.tsv').open(encoding='utf-8-sig') as reader:
        for row in csv.reader((line for line in reader if not line.startswith('#')), delimiter='\t'):
            if not row:
                continue
            definitions[row[0]] = row
            if row[6] == 'true':
                definitions[row[0] + '_anal'] = row
    actions = args.out / 'runtime/UserData/KoiSyncMainGame/Actions'
    selected, playback, groups, invalid = {}, {}, {}, []
    for group in GROUPS:
        start, done, table = [args.out / ('batch-' + group + suffix) for suffix in ['-started.txt', '-complete.txt', '.tsv']]
        if not all(p.exists() for p in [start, done, table]) or utc_file(done) < utc_file(start):
            groups[group] = {'complete': False}
            continue
        since, until = utc_file(start), utc_file(done)
        rows = list(csv.DictReader(table.open(encoding='utf-8-sig'), delimiter='\t'))
        accepted, errors = [], []
        for row in rows:
            key = row['action'] + ('_anal' if group == 'anal' else '')
            targeted = group == 'contact' and key in ['houshi_32', 'houshi_33']
            if key not in definitions or expected_group(key) != group and not targeted:
                errors.append('unexpected action ' + key)
                continue
            requested = row.get('requested', '')
            if row['bound'] != 'True' or int(row['samples']) < 10 or not requested or (requested not in ['native-item', 'native-playback'] and row['stage'] != requested):
                errors.append(key + '/' + row['stage'] + ' did not maintain requested native playback')
                continue
            stage = row['stage']
            accepted.append(key + '/' + stage)
            playback.setdefault(key, []).append(stage)
            folder = actions / key / 'Recordings' / stage
            candidates = sorted(folder.glob('*.take.xml'), reverse=True)
            for path in candidates:
                if not since - 1 <= path.stat().st_mtime <= until + 1:
                    continue
                try:
                    meta = ET.parse(path).getroot().attrib
                    if meta.get('complete') != 'true' and group != 'peeping':
                        continue
                    take = check_take(path, allow_partial=group == 'peeping')
                    if take['action'] != key or take['stage'] != stage:
                        raise ValueError('action or stage metadata differs')
                    selected[key + '/' + stage] = take
                    break
                except (ValueError, KeyError, OSError, ET.ParseError) as ex:
                    invalid.append({'metadata': str(path), 'reason': str(ex)})
        groups[group] = {'complete': True, 'rows': len(rows), 'accepted': len(accepted), 'errors': errors,
                         'startedUtc': datetime.fromtimestamp(since, timezone.utc).isoformat(),
                         'completedUtc': datetime.fromtimestamp(until, timezone.utc).isoformat()}
    missing_playback = []
    missing_recordings = []
    for key in definitions:
        observed = playback.get(key, [])
        expected = ['A_WLoop', 'A_SLoop'] if key.endswith('_anal') else ['WLoop', 'SLoop']
        if key.startswith(('aibu_', 'peeping_')):
            if not observed:
                missing_playback.append(key)
            if not any(t['action'] == key for t in selected.values()):
                missing_recordings.append(key)
        else:
            for stage in expected:
                if stage not in observed:
                    missing_playback.append(key + '/' + stage)
                if key + '/' + stage not in selected:
                    missing_recordings.append(key + '/' + stage)
    report = {'solver': SOLVER, 'createdUtc': datetime.now(timezone.utc).isoformat(),
              'nativeActions': 96, 'analVariants': len(definitions) - 96, 'configuredActions': len(definitions),
              'nativePlaybackActions': len(playback), 'recordedActions': len({t['action'] for t in selected.values()}),
              'completeTakes': sum(t['complete'] for t in selected.values()),
              'partialTakes': sum(not t['complete'] for t in selected.values()), 'axisFiles': len(selected) * 6,
              'recordedFrames': sum(t['frames'] for t in selected.values()),
              'missingPlayback': missing_playback, 'missingRecordings': missing_recordings,
              'invalidTakes': invalid, 'groups': groups, 'takes': list(selected.values()),
              'quantizedStationary': [k for k,t in selected.items() if all(lo == hi for lo,hi in t['axisRanges'])],
              'hardwareConnected': False, 'deviceValidation': 'not performed',
              'fixture': 'Native default actors and native animated interaction items in an isolated game runtime. Native phase inputs maintained by opt-in test probe. No fixture calibration imported into player profiles.'}
    report['passed'] = not missing_playback and not missing_recordings and not invalid and not any(g.get('errors') for g in groups.values())
    (args.out / 'validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    if args.export:
        args.export.mkdir(parents=True, exist_ok=True)
        for take in selected.values():
            target = args.export / 'Actions' / take['action'] / 'Recordings' / take['stage']
            target.mkdir(parents=True, exist_ok=True)
            for filename in take['files']:
                source = Path(filename)
                shutil.copy2(source, target / source.name)
            if take['complete']:
                (target / 'latest.txt').write_text(Path(take['metadata']).name.replace('.take.xml', '.funscript'), encoding='utf-8')
        (args.export / 'validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: report[k] for k in ['passed', 'configuredActions', 'nativePlaybackActions', 'recordedActions', 'completeTakes', 'partialTakes', 'axisFiles', 'recordedFrames', 'missingPlayback', 'missingRecordings', 'invalidTakes']}, ensure_ascii=False, indent=2))
    if args.strict and not report['passed']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
