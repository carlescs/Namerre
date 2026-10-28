#!/bin/sh
# Pre-commit secret scan (run by lefthook). Betterleaks is a standalone binary
# (https://github.com/betterleaks/betterleaks/releases); skip when it is not installed,
# since CI scans every push and pull request anyway.
if ! command -v betterleaks >/dev/null 2>&1; then
  echo "betterleaks not installed; skipping secret scan"
  exit 0
fi
exec betterleaks git --pre-commit --staged --redact --no-banner --verbose
