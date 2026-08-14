from pathlib import Path
import sys

if len(sys.argv) != 3:
    raise SystemExit('usage: filter_patch.py INPUT OUTPUT')

src = Path(sys.argv[1]).read_text(encoding='utf-8')
exclude = {'Services/DolaProtocolInspector.cs', 'Tests/verify_project.py'}
sections = []
current = []
for line in src.splitlines(keepends=True):
    if line.startswith('diff --git '):
        if current:
            sections.append(''.join(current))
        current = [line]
    else:
        current.append(line)
if current:
    sections.append(''.join(current))

kept = []
for section in sections:
    first = section.splitlines()[0]
    path = first.split(' b/', 1)[1]
    if path not in exclude:
        kept.append(section)

Path(sys.argv[2]).write_text(''.join(kept), encoding='utf-8', newline='')
print(f'filtered patch: kept={len(kept)} excluded={len(sections)-len(kept)}')
