#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

# Must match the "Code-metrics and SOLID-leaning rules" block in .editorconfig.
solid_rules=(
  CA1501
  CA1502
  CA1505
  CA1506
  CA1000
  CA1001
  CA1002
  CA1010
  CA1012
  CA1033
  CA1040
  CA1047
  CA1051
  CA1065
  CA1715
  CA1822
)

if command -v git >/dev/null 2>&1 && git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  mapfile -t cs_files < <(git ls-files --cached --others --exclude-standard '*.cs')
else
  mapfile -t cs_files < <(find . -type f -name '*.cs' -not -path './.git/*' | sed 's#^\./##')
fi

if [ "${#cs_files[@]}" -eq 0 ]; then
  echo "[solid-suppression-gate] OK - no C# files found"
  exit 0
fi

solid_rule_list="${solid_rules[*]}"

findings="$(
  SOLID_RULES="${solid_rule_list}" perl -0ne '
    BEGIN {
      %solid = map { $_ => 1 } split /\s+/, $ENV{"SOLID_RULES"};
    }

    while (/\[(?:[A-Za-z_][A-Za-z0-9_]*\s*:\s*)?(?:global::)?(?:[A-Za-z_][A-Za-z0-9_]*\.)*SuppressMessage(?:Attribute)?\s*\((.*?)\)\]/sg) {
      my $match_start = $-[0];
      my $attribute = $1;
      next unless $attribute =~ /"([A-Z]{2}\d{4})"/;

      my $rule = $1;
      next unless $solid{$rule};

      my $prefix = substr($_, 0, $match_start);
      my $line = 1 + ($prefix =~ tr/\n//);
      print "$ARGV:$line:$rule\n";
    }

    while (/^\s*#pragma\s+warning\s+disable\s+([A-Z]{2}\d{4}(?:\s*,\s*[A-Z]{2}\d{4})*)/mg) {
      my $match_start = $-[0];
      my $disabled = $1;
      while ($disabled =~ /([A-Z]{2}\d{4})/g) {
        my $rule = $1;
        next unless $solid{$rule};

        my $prefix = substr($_, 0, $match_start);
        my $line = 1 + ($prefix =~ tr/\n//);
        print "$ARGV:$line:$rule\n";
      }
    }
  ' "${cs_files[@]}"
)"

if [ -z "${findings}" ]; then
  echo "[solid-suppression-gate] OK - no SOLID warning suppression found"
  exit 0
fi

echo "[solid-suppression-gate] FAIL - SOLID analyzer suppressions are not allowed"
echo ""
echo "Findings:"
echo "${findings}" | sort
echo ""
echo "Histogram:"
echo "${findings}" | cut -d: -f3 | sort | uniq -c | sort -nr | awk '{ printf "  %-6s %d\n", $2, $1 }'
echo ""
echo "Rule documentation:"
echo "${findings}" \
  | cut -d: -f3 \
  | sort -u \
  | awk '{ printf "  %s https://learn.microsoft.com/de-de/dotnet/fundamentals/code-analysis/quality-rules/%s\n", $1, tolower($1) }'
echo ""
echo "Refactor the code so the analyzer passes without a SOLID suppression."
exit 1
