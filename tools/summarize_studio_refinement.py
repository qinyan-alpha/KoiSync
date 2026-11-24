"""Summarize the normal-playback proof produced by fullplay40.py."""
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path(sys.argv[1])
status = json.loads((root / 'status.json').read_text(encoding='utf-8-sig'))
summary = {'status': status['status'], 'cards': []}
for card in status['cards']:
    if card['status'] != 'complete':
        continue
    stem = card['id']
    rows = json.loads((root / (stem + '-rows.json')).read_text(encoding='utf-8-sig'))
    chapters = list(ET.parse(root / (stem + '-chapters.xml')).getroot())
    transitions = []
    last = None
    for row in rows:
        if row['binding'] != last:
            transitions.append({key: row.get(key) for key in ('time', 'binding', 'contactMm', 'engagement', 'image')})
            last = row['binding']
    flickers = []
    for before, middle, after in zip(transitions, transitions[1:], transitions[2:]):
        if before['binding'] == after['binding'] and after['time'] - middle['time'] < 2.5:
            flickers.append({'start': middle['time'], 'end': after['time'], 'binding': middle['binding']})
    scripts = []
    for suffix in ('', '.surge', '.sway', '.twist', '.roll', '.pitch'):
        path = root / (stem + suffix + '.funscript')
        data = json.loads(path.read_text(encoding='utf-8-sig'))['actions']
        scripts.append({'file': path.name, 'keys': len(data), 'start': data[0]['at'], 'end': data[-1]['at'],
                        'strictlyOrdered': all(a['at'] < b['at'] for a, b in zip(data, data[1:])),
                        'inRange': all(0 <= a['pos'] <= 100 for a in data)})
    summary['cards'].append({'id': stem, 'actors': card['actors'], 'duration': card['duration'],
                             'recordedFrames': card['frames'], 'actorListReadsDuringPlayback': card['actorReadsAfter'] - card['actorReads'],
                             'hiddenBindings': sum(row['binding'] is not None and not row['visible'] for row in rows),
                             'unboundSamples': sum(row['binding'] is None for row in rows),
                             'chapterCount': len(chapters), 'climaxChapters': sum(c.get('climaxDetected') == 'true' for c in chapters),
                             'chaptersWithoutCalibration': sum(c.get('source') != '-1' and c.find('calibration') is None for c in chapters),
                             'transitions': transitions, 'briefRoundTrips': flickers, 'scripts': scripts})
(root / 'validation-summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(summary, ensure_ascii=False, indent=2))
