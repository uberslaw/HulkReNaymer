from __future__ import annotations

import json
import os
import shutil
import subprocess
from typing import Any

SANDBOX = r"""
'use strict';
const vm = require('vm');
let input = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => input += chunk);
process.stdin.on('end', () => {
  const payload = JSON.parse(input);
  const ctx = {
    name: payload.name,
    ext: payload.ext,
    newName: payload.newName,
    index: payload.index,
    folder: payload.folder,
    size: payload.size,
    path: payload.path,
    isDir: payload.isDir,
    created: payload.created,
    modified: payload.modified,
    accessed: payload.accessed,
    exif: payload.exif || {},
    id3: payload.id3 || {},
    props: payload.props || {},
  };
  vm.runInNewContext(payload.code, ctx, { timeout: 250, displayErrors: true });
  process.stdout.write(JSON.stringify({ newName: String(ctx.newName ?? payload.newName) }));
});
"""


def node_binary() -> str | None:
    return shutil.which("node") or (os.path.exists("/exec-daemon/node") and "/exec-daemon/node") or None


def run_javascript(code: str, context: dict[str, Any]) -> tuple[str | None, str]:
    binary = node_binary()
    if not binary:
        return None, "JavaScript rules need Node.js on PATH"
    payload = dict(context)
    payload["code"] = code
    try:
        completed = subprocess.run(
            [binary, "-e", SANDBOX],
            input=json.dumps(payload).encode("utf-8"),
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=1.5,
            check=False,
        )
    except subprocess.TimeoutExpired:
        return None, "JavaScript rule timed out"
    if completed.returncode != 0:
        err = completed.stderr.decode("utf-8", errors="replace").strip()
        return None, err or "JavaScript rule failed"
    try:
        data = json.loads(completed.stdout.decode("utf-8"))
    except json.JSONDecodeError:
        return None, "JavaScript rule produced invalid output"
    return data.get("newName"), ""
