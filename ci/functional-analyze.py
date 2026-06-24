#!/usr/bin/env python3
import base64
import gzip
from pathlib import Path

payload = Path('functional_test/functional-analyze-v404.py.gz.b64').read_text(encoding='ascii').strip()
source = gzip.decompress(base64.b64decode(payload)).decode('utf-8')
exec(compile(source, 'functional-analyze-v404.py', 'exec'), {'__name__': '__main__'})
