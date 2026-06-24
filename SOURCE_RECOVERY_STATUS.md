# JingHua source recovery status

The repository contains a base64-encoded XZ source archive split into six expected files:

`source_parts/part-00` through `source_parts/part-05`.

Current main-branch audit:

- present: `part-00`, `part-01`, `part-03`, `part-04`, `part-05`
- missing: **`part-02`**
- result: the V4.0.1 Android source archive cannot be reconstructed yet

After uploading the original `part-02`, run:

```bash
python tools/reconstruct_source.py \
  --parts-dir source_parts \
  --output build/JingHuaV4_0_1-source.tar.xz \
  --extract-dir build/source
```

The tool verifies base64, XZ magic, XZ decompression, and optional tar extraction. The workflow reports missing parts instead of silently treating the repository as a valid backup.
