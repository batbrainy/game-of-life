#!/bin/sh
# Measure fresh simulations, different-board concurrency, and same-board lock contention.
# Usage: scripts/measure-worst-case.sh [base-url] [concurrency]
# Run against an isolated stack with default size/iteration limits. Concurrency defaults to min(container CPUs, 8).
# Timing is observational, not a hard latency assertion. Same-board contention may return 503 after the lock timeout.
set -eu
export LC_ALL=C
base_url=${1:-http://localhost:8080}
concurrency=${2:-}
if [ -z "$concurrency" ]; then
    concurrency=$(cd "$(dirname "$0")/.." && docker compose exec -T api nproc)
    if [ "$concurrency" -gt 8 ]; then concurrency=8; fi
fi
case "$concurrency" in ''|*[!0-9]*) echo 'Concurrency must be an integer from 1 to 16'; exit 1;; esac
if [ "$concurrency" -lt 1 ] || [ "$concurrency" -gt 16 ]; then echo 'Concurrency must be 1 to 16'; exit 1; fi
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT
boards_url=$base_url/api/v1/boards

awk 'BEGIN {
    x=42; printf "{\"cells\":[";
    for (r=0;r<256;r++) {
        if(r) printf ","; printf "[";
        for(c=0;c<256;c++) {
            if(c) printf ","; x=x*16807%2147483647; printf "%d", (x<1073741824);
        }
        printf "]";
    }
    printf "]}";
}' > "$work_dir/board.json"

upload() {
    result=$(curl --silent --show-error --fail --max-time 60 -H 'Content-Type: application/json' --data-binary "@$work_dir/board.json" "$boards_url")
    id=$(printf '%s' "$result" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p')
    [ -n "$id" ] || exit 1
    printf '%s/%s' "$boards_url" "$id"
}
measure() {
    curl --silent --show-error --max-time 90 --request "$1" --output "$work_dir/$3.body" \
        --write-out '%{http_code} %{time_total}\n' "$2" > "$work_dir/$3.time"
}
report() {
    label=$1
    expected=$2
    allow_busy=$3
    count=$4
    i=1
    while [ "$i" -le "$count" ]; do
        status=$(cut -d ' ' -f 1 "$work_dir/$i.time")
        if [ "$status" != "$expected" ]; then
            if [ "$allow_busy" != yes ] || [ "$status" != 503 ] || ! grep -q 'Board is busy' "$work_dir/$i.body"; then
                echo "FAIL $label: unexpected $status"; cat "$work_dir/$i.body"; exit 1
            fi
        fi
        i=$((i+1))
    done
    printf '%s\n' "$label"
    i=1
    while [ "$i" -le "$count" ]; do cat "$work_dir/$i.time"; i=$((i+1)); done
    awk '{sum+=$2; if(NR==1||$2<min)min=$2; if($2>max)max=$2} END {printf "min %.3fs, mean %.3fs, max %.3fs\n",min,sum/NR,max}' "$work_dir"/*.time
    rm "$work_dir"/*.time "$work_dir"/*.body
}
board_url=$(upload)
i=1
while [ "$i" -le 5 ]; do measure GET "$board_url/generations/500" "$i"; i=$((i+1)); done
report 'GET projection, sequential' 200 no 5
i=1
while [ "$i" -le 5 ]; do measure POST "$board_url/final" "$i"; i=$((i+1)); done
# This fixed pattern must exhaust the limit; 200 would measure an early or cached result instead.
report 'POST final, sequential full-limit searches' 422 no 5
i=1
while [ "$i" -le "$concurrency" ]; do upload > "$work_dir/url.$i"; i=$((i+1)); done
i=1
while [ "$i" -le "$concurrency" ]; do measure POST "$(cat "$work_dir/url.$i")/final" "$i" & i=$((i+1)); done
wait
report 'POST final, different GUIDs concurrently' 422 no "$concurrency"
i=1
while [ "$i" -le "$concurrency" ]; do measure POST "$board_url/final" "$i" & i=$((i+1)); done
wait
report 'POST final, same GUID concurrently (serialized; 503 allowed on lock timeout)' 422 yes "$concurrency"
state=$(curl --silent --show-error --fail "$board_url")
case "$state" in *'"generation":0,'*'"status":"Active"'*) ;; *) echo 'FAIL failed searches changed persisted state'; exit 1;; esac
echo 'PASS all measurements and persistence checks'
