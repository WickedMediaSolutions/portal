import os
p = r"C:\Users\mcdor\Portal\release\RitesOfPassage-ThePortal-beta\Portal.Core.dll"
raw = open(p,"rb").read()
i = raw.find(b"C:\\\\Users\\\\mcdor")
if i < 0:
    i = raw.find(b"C:\\Users\\mcdor")
print("offset:", i)
lo = max(0, i-120); hi = min(len(raw), i+220)
print(repr(raw[lo:hi]))
