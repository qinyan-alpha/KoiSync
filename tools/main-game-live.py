"""Drive only the opt-in main-game validation probe; preserve the user's Studio."""
import argparse
from pathlib import Path
import time
import xml.etree.ElementTree as ET

OUT = Path(__file__).resolve().parents[1] / 'artifacts/main-game-v010'

def state():
    try:
        return ET.parse(OUT / 'live.xml').getroot().attrib
    except (OSError, ET.ParseError):
        return {}

def send(*lines):
    target = OUT / 'command.txt'
    deadline = time.monotonic() + 8
    while target.exists() and time.monotonic() < deadline:
        time.sleep(.1)
    if target.exists():
        raise RuntimeError('The live probe did not consume the previous command')
    target.write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print('Command:', lines, flush=True)

def wait(predicate, seconds=30):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        current = state()
        if predicate(current):
            return current
        time.sleep(.2)
    raise TimeoutError(str(state()))

parser = argparse.ArgumentParser()
parser.add_argument('--boot', action='store_true')
parser.add_argument('--mode', type=int, default=0)
parser.add_argument('--card', default='')
parser.add_argument('--action')
parser.add_argument('--stage')
parser.add_argument('--record', action='store_true')
parser.add_argument('--capture')
parser.add_argument('--out',type=Path)
args = parser.parse_args()
if args.out:
    OUT=args.out.resolve()
if args.boot:
    entry=wait(lambda s:s.get('scene') in ('Title','FreeH'),40)
    if entry.get('scene') == 'Title':
        send('freeh')
    wait(lambda s: s.get('scene') == 'FreeH')
    if args.card:
        send('setup-freeh', str(args.mode), args.card)
    else:
        send('setup-freeh', str(args.mode))
    wait(lambda s: s.get('scene') == 'H')
    time.sleep(2)
    send('dismiss-tutorial')
    wait(lambda s: bool(s.get('action')) and not s['action'].startswith('none_') and s.get('plugin') == 'True', 40)
if args.action:
    send('action', args.action)
    wait(lambda s: s.get('action') == args.action)
if args.stage:
    send('state', args.stage)
    wait(lambda s: s.get('stage') == args.stage)
if args.record:
    send('record', 'true')
    wait(lambda s: s.get('recording') == 'True')
if args.capture:
    send('capture', args.capture)
print(state(), flush=True)
