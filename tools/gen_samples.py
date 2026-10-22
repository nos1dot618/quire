"""Generate assorted test documents for Quire into the folder given as argv[1]."""

import hashlib
import json
import os
import random
import struct
import sys
import zipfile
import zlib

out = sys.argv[1]
random.seed(519)
os.makedirs(out, exist_ok=True)


def w(name, text, mode="w"):
    p = os.path.join(out, name)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(
        p,
        mode,
        encoding=None if "b" in mode else "utf-8",
        newline="" if "b" not in mode else None,
    ) as f:
        f.write(text)
    return p


def read_text(*parts):
    with open(os.path.join(out, *parts), encoding="utf-8") as f:
        return f.read()


def read_bytes(*parts):
    with open(os.path.join(out, *parts), "rb") as f:
        return f.read()


def sha():
    return hashlib.sha1(str(random.random()).encode()).hexdigest()


# --- plain text ---------------------------------------------------------------
w("text/commit-shas.txt", "\n".join(sha() for _ in range(12)) + "\n")
w(
    "text/branches.txt",
    "\n".join(
        [
            "main",
            "release/2.4",
            "feature/cell-reorder",
            "fix/autosave-race",
            "lakshay/quire-export-pdf",
            "hotfix/519-null-image",
            "chore/bump-markdig",
        ]
    )
    + "\n",
)
w(
    "text/paths.txt",
    "\n".join(
        [
            r"C:\Users\me\source\quire\src\Quire.App\UI\MainWindow.xaml",
            r"C:\Program Files\dotnet\dotnet.exe",
            r"\\fileserver\share\builds\2026-10-21\quire-setup.exe",
            "/home/dev/repos/quire/tests/Quire.Tests/StorageTests.cs",
            r"D:\Screenshots\failure 2026-10-20 14.33.12.png",
        ]
    )
    + "\n",
)
w(
    "text/notes.txt",
    "Remember to rebase before merging.\nThe flaky test is StorageTests.SaveIsAtomic, only on CI.\n"
    "Ask about the Windows 10 21H2 build.\n\n  Indented line with trailing spaces   \n\tTab-indented line\n",
)
w(
    "text/unicode.txt",
    "Ünïcödé — “smart quotes”, ellipsis…, emoji 🧪🚀, CJK 文档, RTL שלום, math ∑∫√, NBSP:\u00a0end\n",
)
w("text/crlf-endings.txt", "line one\r\nline two\r\nline three\r\n")
w("text/empty.txt", "")
w("text/single-line-no-newline.txt", "4f2a9c1e8b7d6a5f3e2d1c0b9a8f7e6d5c4b3a21")
w("text/very-long-line.txt", " ".join(f"token{i}" for i in range(5000)) + "\n")
words = [
    "lorem",
    "ipsum",
    "dolor",
    "sit",
    "amet",
    "consectetur",
    "adipiscing",
    "elit",
    "sed",
    "do",
    "eiusmod",
    "tempor",
    "incididunt",
    "ut",
    "labore",
]
w(
    "text/large-lorem.txt",
    "\n\n".join(" ".join(random.choice(words) for _ in range(random.randint(40, 120))) for _ in range(800)),
)
w("text/latin1.txt", "caf\xe9 na\xefve r\xe9sum\xe9\n".encode("latin-1"), "wb")
w(
    "text/utf16-bom.txt",
    "\ufeffUTF-16 encoded file with BOM\n".encode("utf-16-le"),
    "wb",
)

# --- markdown -----------------------------------------------------------------
w(
    "markdown/pr-519.md",
    """# Pull request #519

Fixes a null reference when an **image element** has no asset.

- Branch: `hotfix/519-null-image`
- Commit: `"""
    + sha()
    + """`
- Link: [github.com/example/quire/pull/519](https://github.com/example/quire/pull/519)

## Test plan

1. Open `broken.quire`
2. Delete the asset from the archive
3. Reopen, expect a placeholder

> Reviewer note: please also check GIFs.

| Case | Before | After |
|---|---|---|
| Missing PNG | crash | placeholder |
| Missing GIF | crash | placeholder |

```csharp
if (asset is null) return Placeholder.MissingImage;
```

- [x] Unit test
- [ ] Manual test on Windows 10
""",
)
w(
    "markdown/release-checklist.md",
    "## Release 2.4 checklist\n\n"
    + "\n".join(
        f"- [{'x' if i % 3 else ' '}] Step {i}: " + " ".join(random.choice(words) for _ in range(6))
        for i in range(1, 16)
    )
    + "\n",
)
w(
    "markdown/edge-cases.md",
    "# Heading 1\n## Heading 2\n###### Heading 6\n\n*italic* _also italic_ ***bold italic*** ~~strike~~\n\n"
    "Inline `code` and a <kbd>Ctrl</kbd>+<kbd>S</kbd> html tag.\n\nAutolink: https://example.com/path?q=1&x=2\n\n"
    "![missing image](./nope.png)\n\n---\n\n1. one\n   - nested\n     - deeper\n2. two\n\nFootnote ref[^1].\n\n[^1]: The footnote.\n\n"
    "$$E = mc^2$$\n\n<details><summary>HTML block</summary>hidden</details>\n",
)

# --- code and logs ------------------------------------------------------------
w(
    "code/Snippet.cs",
    """using System.IO.Compression;

namespace Scratch;

public static class ArchiveProbe
{
    public static int CountEntries(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.Count;
    }
}
""",
)
w(
    "code/script.py",
    "import sys\n\ndef main(argv):\n    for a in argv[1:]:\n        print(a.upper())\n\nif __name__ == '__main__':\n    main(sys.argv)\n",
)
w(
    "code/build.ps1",
    "$ErrorActionPreference = 'Stop'\ndotnet build -c Release\nif ($LASTEXITCODE -ne 0) { throw 'build failed' }\n",
)
w(
    "code/query.sql",
    "SELECT id, name\nFROM cells\nWHERE document_id = @doc\nORDER BY position;\n",
)
w(
    "code/config.json",
    json.dumps(
        {
            "theme": "system",
            "autosaveSeconds": 30,
            "recent": ["C:/a.quire", "D:/b.quire"],
            "nested": {"x": [1, 2, {"y": None}]},
        },
        indent=2,
    ),
)
w(
    "code/settings.yaml",
    "theme: dark\nzoom: 1.25\nexport:\n  includeCellNames: true\n  pdf:\n    margins: [20, 20, 20, 20]\n",
)
w(
    "code/page.html",
    "<!doctype html><html><head><title>Sample</title></head><body><h1>Hello</h1><p>A <a href='https://example.com'>link</a>.</p></body></html>\n",
)
w(
    "code/data.csv",
    "sha,author,date,message\n"
    + "\n".join(f'{sha()[:10]},dev{i % 4},2026-10-{i + 1:02d},"fix, part {i}"' for i in range(20))
    + "\n",
)
ts = 0
log = []
for _ in range(400):
    ts += random.randint(1, 900)
    lvl = random.choice(["INFO"] * 6 + ["WARN", "ERROR", "DEBUG"])
    log.append(
        f"2026-10-21T10:{(ts // 60000) % 60:02d}:{(ts // 1000) % 60:02d}.{ts % 1000:03d}Z [{lvl}] worker-{random.randint(1, 4)} "
        + " ".join(random.choice(words) for _ in range(8))
    )
w("logs/app.log", "\n".join(log) + "\n")
w(
    "logs/test-failure.txt",
    """$ dotnet test
  Determining projects to restore...
  Quire.Tests -> C:\\src\\quire\\tests\\Quire.Tests\\bin\\Debug\\net10.0\\Quire.Tests.dll
[xUnit.net 00:00:01.12]     Quire.Tests.StorageTests.SaveIsAtomic [FAIL]
  Failed Quire.Tests.StorageTests.SaveIsAtomic [38 ms]
  Error Message:
   Assert.True() Failure
Expected: True
Actual:   False
  Stack Trace:
     at Quire.Tests.StorageTests.SaveIsAtomic() in C:\\src\\quire\\tests\\Quire.Tests\\StorageTests.cs:line 42

Failed!  - Failed:     1, Passed:    27, Skipped:     0, Total:    28, Duration: 1 s
""",
)
w(
    "logs/ansi-colors.txt",
    "\x1b[32mPASS\x1b[0m storage\n\x1b[31mFAIL\x1b[0m export\n\x1b[1;33mWARN\x1b[0m slow test\n",
)

# --- links --------------------------------------------------------------------
w(
    "links/urls.txt",
    "\n".join(
        [
            "https://github.com/example/quire/pull/519",
            "https://github.com/example/quire/commit/" + sha(),
            "https://learn.microsoft.com/dotnet/desktop/wpf/",
            "http://localhost:5000/api/health",
            "https://example.com/a%20path/with?query=1&other=two#frag",
            "file:///C:/Users/me/Desktop/notes.txt",
            "mailto:someone@example.com",
            "not a url: example dot com",
        ]
    )
    + "\n",
)
w("links/shortcut.url", "[InternetShortcut]\nURL=https://github.com/example/quire\n")


# --- images (pure Python encoders; no Pillow dependency) ----------------------
def rects(wd, ht, n=12, ch=3):
    """Return a pixel function painting a background plus n random rectangles."""
    bg = tuple(random.randint(0, 255) for _ in range(ch))
    rs = []
    for _ in range(n):
        x = sorted(random.randint(0, wd) for _ in range(2))
        y = sorted(random.randint(0, ht) for _ in range(2))
        rs.append((x[0], y[0], x[1], y[1], tuple(random.randint(0, 255) for _ in range(ch))))

    def px(x, y):
        for x0, y0, x1, y1, c in reversed(rs):
            if x0 <= x < x1 and y0 <= y < y1:
                return c
        return bg

    return px


def png(name, wd, ht, px=None, alpha=False):
    ch = 4 if alpha else 3
    px = px or rects(wd, ht, ch=ch)
    raw = bytearray()
    for y in range(ht):
        raw.append(0)
        row = [px(x, y) for x in range(wd)]
        for c in row:
            raw.extend(c)

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)

    data = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", wd, ht, 8, 6 if alpha else 2, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(bytes(raw), 6))
        + chunk(b"IEND", b"")
    )
    w("images/" + name, data, "wb")


def bmp(name, wd, ht):
    px = rects(wd, ht)
    pad = (4 - wd * 3 % 4) % 4
    body = bytearray()
    for y in range(ht - 1, -1, -1):
        for x in range(wd):
            body.extend(reversed(px(x, y)))
        body.extend(b"\0" * pad)
    hdr = (
        b"BM"
        + struct.pack("<IHHI", 54 + len(body), 0, 0, 54)
        + struct.pack("<IiiHHIIiiII", 40, wd, ht, 1, 24, 0, len(body), 2835, 2835, 0, 0)
    )
    w("images/" + name, hdr + body, "wb")


def gif(name, wd, ht, frames, palette, delay=5):
    """frames: list of functions (x, y) -> palette index (0..255). Uses 9-bit codes with frequent clears."""

    def lzw(indices):
        codes = []
        for i, v in enumerate(indices):
            if i % 250 == 0:
                codes.append(256)
            codes.append(v)
        codes.append(257)
        acc, n, out_ = 0, 0, bytearray()
        for c in codes:
            acc |= c << n
            n += 9
            while n >= 8:
                out_.append(acc & 0xFF)
                acc >>= 8
                n -= 8
        if n:
            out_.append(acc & 0xFF)
        blocks = bytearray([8])
        for i in range(0, len(out_), 255):
            blk = out_[i : i + 255]
            blocks.append(len(blk))
            blocks.extend(blk)
        return bytes(blocks + b"\0")

    pal = bytearray()
    for c in palette + [(0, 0, 0)] * (256 - len(palette)):
        pal.extend(c)
    data = bytearray(b"GIF89a" + struct.pack("<HHBBB", wd, ht, 0xF7, 0, 0) + pal)
    if len(frames) > 1:
        data += b"\x21\xff\x0bNETSCAPE2.0\x03\x01\x00\x00\x00"
    for f in frames:
        data += b"\x21\xf9\x04\x00" + struct.pack("<H", delay) + b"\x00\x00"
        data += b"\x2c" + struct.pack("<HHHHB", 0, 0, wd, ht, 0)
        data += lzw([f(x, y) for y in range(ht) for x in range(wd)])
    w("images/" + name, bytes(data + b"\x3b"), "wb")


png("screenshot-1920x1080.png", 1920, 1080)
png("small-64x64.png", 64, 64)
png("tall-400x3000.png", 400, 3000)
png("wide-4000x300.png", 4000, 300)
png(
    "transparent.png",
    300,
    200,
    lambda x, y: (40, 120, 220, (x * 255) // 300),
    alpha=True,
)
png("tiny-1x1.png", 1, 1, lambda x, y: (255, 0, 0))
png(
    "huge-4000x3000.png",
    4000,
    3000,
    lambda x, y: ((x // 8) & 255, (y // 8) & 255, ((x ^ y) >> 4) & 255),
)
bmp("diagram.bmp", 320, 240)
spin = [lambda x, y, i=i: 1 if (x - (20 + i * 9)) ** 2 + (y - 60) ** 2 < 400 else 0 for i in range(24)]
gif("spinner.gif", 240, 120, spin, [(30, 30, 30), (80, 200, 255)])
gif(
    "static.gif",
    50,
    50,
    [lambda x, y: (x // 10 + y // 10) % 2],
    [(255, 255, 255), (200, 40, 40)],
)
w("images/not-really.png", "this is a text file with a .png extension\n")
w(
    "images/truncated.png",
    read_bytes("images", "small-64x64.png")[:60],
    "wb",
)

# --- misc binaries ------------------------------------------------------------
w("binary/random.bin", os.urandom(4096), "wb")
with zipfile.ZipFile(os.path.join(out, "binary", "archive.zip"), "w") as z:
    z.writestr("readme.txt", "inside a zip\n")
    z.writestr("nested/data.json", "{}")
w(
    "binary/minimal.pdf",
    b"%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n"
    b"3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n",
    "wb",
)

# --- odd file names -----------------------------------------------------------
w("names/file with spaces.txt", "spaces in name\n")
w("names/ünïcode-名前.txt", "unicode file name\n")
w("names/no-extension", "no extension\n")
w("names/.hidden-dotfile", "dotfile\n")
w("names/multiple.dots.in.name.tar.gz.txt", "dots\n")


# --- hand-made .quire documents (spec section 5.1 layout) ----------------------
def quire(name, manifest, assets=None, raw_manifest=None):
    with zipfile.ZipFile(os.path.join(out, "quire", name), "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr(
            "document.json",
            raw_manifest if raw_manifest is not None else json.dumps(manifest, indent=2),
        )
        for path, src in (assets or {}).items():
            z.write(src, path)


os.makedirs(os.path.join(out, "quire"), exist_ok=True)


def el(t, **kw):
    return {
        "id": hashlib.md5(str(random.random()).encode()).hexdigest(),
        "type": t,
        **kw,
    }


def cell(name, *elements):
    return {
        "id": hashlib.md5(str(random.random()).encode()).hexdigest(),
        "name": name,
        "elements": list(elements),
    }


png = os.path.join(out, "images", "screenshot-1920x1080.png")
gif = os.path.join(out, "images", "spinner.gif")
doc = {
    "formatVersion": 1,
    "title": "PR 519 notes",
    "cells": [
        cell(
            "PR #519",
            el("markdown", text="## Summary\nFixes the null image crash."),
            el("link", url="https://github.com/example/quire/pull/519", title="PR #519"),
            el(
                "code",
                language="csharp",
                text="if (asset is null) return Placeholder.MissingImage;",
            ),
        ),
        cell(
            "Test failure",
            el("text", text="Only fails on CI."),
            el("image", asset="assets/image-001.png"),
            el("divider"),
            el("gif", asset="assets/animation-001.gif"),
        ),
        cell(
            "Useful references",
            el("text", text=sha()),
            el("fileReference", path=r"C:\Users\me\source\quire\docs\SPEC.md"),
            el("attachment", asset="assets/attachment-001.txt", fileName="notes.txt"),
        ),
        cell("", el("text", text="A cell with no name.")),
    ],
}
quire(
    "sample.quire",
    doc,
    {
        "assets/image-001.png": png,
        "assets/animation-001.gif": gif,
        "assets/attachment-001.txt": os.path.join(out, "text", "notes.txt"),
    },
)
quire("empty-document.quire", {"formatVersion": 1, "title": "Empty", "cells": []})
quire(
    "missing-asset.quire",
    {**doc, "title": "Missing asset"},
    {"assets/image-001.png": png},
)
quire("future-version.quire", {**doc, "formatVersion": 999})
quire("invalid-json.quire", None, raw_manifest='{ "formatVersion": 1, "cells": [ ')
quire(
    "unknown-element.quire",
    {
        "formatVersion": 1,
        "title": "Unknown",
        "cells": [cell("x", el("hologram", text="?"))],
    },
)
with zipfile.ZipFile(os.path.join(out, "quire", "no-manifest.quire"), "w") as z:
    z.writestr("readme.txt", "no document.json here")
w("quire/not-a-zip.quire", "plain text pretending to be a quire file\n")
big = {
    "formatVersion": 1,
    "title": "Big",
    "cells": [
        cell(
            f"Cell {i}",
            *[el("text", text=" ".join(random.choice(words) for _ in range(30))) for _ in range(5)],
        )
        for i in range(500)
    ],
}
quire("large-500-cells.quire", big)

demo = {
    "formatVersion": 1,
    "title": "Preview demo",
    "cells": [
        cell(
            "Markdown preview",
            el(
                "markdown",
                text=read_text("markdown", "pr-519.md"),
            ),
        ),
        cell(
            "Code",
            el(
                "code",
                language="csharp",
                text=read_text("code", "Snippet.cs"),
            ),
            el(
                "code",
                language="sql",
                text=read_text("code", "query.sql"),
            ),
        ),
        cell(
            "GIF",
            el("gif", asset="assets/animation-001.gif"),
            el("text", text="The spinner above should move."),
        ),
    ],
}
quire("preview-demo.quire", demo, {"assets/animation-001.gif": gif})
quire(
    "gif-only.quire",
    {
        "formatVersion": 1,
        "title": "GIF",
        "cells": [cell("GIF", el("gif", asset="assets/animation-001.gif"))],
    },
    {"assets/animation-001.gif": gif},
)

w(
    "README.txt",
    "Assorted throwaway test documents for exercising Quire (paste, drag-and-drop, import, load and export).\n"
    "This folder is gitignored. Regenerate with tools/gen_samples.py <this folder>.\n"
    "images/photo.jpg is converted from the PNG screenshot with System.Drawing (the script has no JPEG encoder).\n",
)

total = sum(len(fs) for _, _, fs in os.walk(out))
print(f"wrote {total} files to {out}")
