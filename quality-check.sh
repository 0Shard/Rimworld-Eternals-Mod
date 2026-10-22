#!/usr/bin/env bash
# Author: 0Shard
# Keep one root cwd for solution loading and report identities.
set -euo pipefail
ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
cd -- "$ROOT"
exec python3 tools/quality_evidence.py run "$@"
