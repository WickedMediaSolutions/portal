"""Release audit: scan distribution binaries for developer paths / QA strings.

.NET stores string literals in the #US metadata heap as UTF-16LE, so both
UTF-8 and UTF-16 encodings must be searched.
"""
import os
import sys

OUT = r"C:\Users\mcdor\Portal\release\RitesOfPassage-ThePortal-beta"

NEEDLES = [
    "C:\\Users\\mcdor",
    "C:/Users/mcdor",
    "Users\\mcdor",
    "MapMaker",
    "keystone\\",
    "keystone/",
    "LiveQa",
    "testuser",
    "__testchar__",
    "ritesrpg.com",
    "localhost",
    "ws://",
    "wss://",
]

files = [
    os.path.join(OUT, f)
    for f in sorted(os.listdir(OUT))
    if os.path.isfile(os.path.join(OUT, f))
]

print(f"scanning {len(files)} file(s) in {OUT}\n")
for path in files:
    with open(path, "rb") as fh:
        raw = fh.read()
    try:
        text_utf8 = raw.decode("utf-8", errors="ignore")
    except Exception:
        text_utf8 = ""
    text_utf16 = raw.decode("utf-16-le", errors="ignore")
    hits = {}
    for needle in NEEDLES:
        n = needle.encode("utf-8")
        c8 = text_utf8.count(needle)
        c16 = text_utf16.count(needle)
        if c8 or c16:
            hits[needle] = (c8, c16)
    name = os.path.basename(path)
    if hits:
        print(f"{name}")
        for k, (a, b) in sorted(hits.items()):
            print(f"    {k!r:24s} utf8={a} utf16={b}")
    else:
        print(f"{name}: (clean)")

print("\nSCAN COMPLETE")
