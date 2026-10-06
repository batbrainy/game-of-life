#!/bin/sh
# Checks a running API end to end: uploads boards, compares the fetch, next generation, generations ahead and
# final state responses with the exact JSON expected, and checks the 400, 404, 422 and 413 errors.
# Usage: scripts/smoke-test.sh [base-url]    (base-url defaults to http://localhost:8080)
# Prints PASS or FAIL for each check and a summary, and exits non-zero when any check fails.

base_url=${1:-http://localhost:8080}
boards_url=$base_url/api/v1/boards
script_dir=$(dirname "$0")
passed=0
failed=0

vertical_blinker='[[0,1,0],[0,1,0],[0,1,0]]'
horizontal_blinker='[[0,0,0],[1,1,1],[0,0,0]]'

glider="[\
[0,1,0,0,0,0,0,0,0,0],\
[0,0,1,0,0,0,0,0,0,0],\
[1,1,1,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0]]"

glider_moved="[\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,1,0,0,0,0,0,0,0],\
[0,0,0,1,0,0,0,0,0,0],\
[0,1,1,1,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0]]"

block_in_corner="[\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,0,0],\
[0,0,0,0,0,0,0,0,1,1],\
[0,0,0,0,0,0,0,0,1,1]]"

# Prints the response body and then, on a line of its own, the status code (000 when no response arrived). The
# 60-second limit exceeds the API's database timeouts (Npgsql's defaults: 15 seconds to connect, 30 for a command),
# so an answer delayed by the database still arrives.
request() {
    curl --silent --show-error --connect-timeout 2 --max-time 60 --write-out '\n%{http_code}' "$@"
}

# Uploads $1, which curl sends as the body: JSON text, @file for a file's contents, or @- for standard input.
upload() {
    request --header 'Content-Type: application/json' --data-binary "$1" "$boards_url"
}

status_of() {
    printf '%s\n' "$1" | tail -n 1
}

body_of() {
    printf '%s\n' "$1" | sed '$d'
}

id_of() {
    printf '%s\n' "$1" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p'
}

title_of() {
    printf '%s\n' "$1" | sed -n 's/.*"title":"\([^"]*\)".*/\1/p'
}

# check NAME RESPONSE STATUS [BODY]: passes when RESPONSE has STATUS and, if BODY is given, exactly that body. An
# empty BODY counts as given.
check() {
    actual_status=$(status_of "$2")
    actual_body=$(body_of "$2")
    if [ "$actual_status" != "$3" ]; then
        printf 'FAIL %s: expected status %s, got %s %s\n' "$1" "$3" "$actual_status" "$actual_body"
        failed=$((failed + 1))
    elif [ "$#" -ge 4 ] && [ "$actual_body" != "$4" ]; then
        printf 'FAIL %s: expected body %s, got %s\n' "$1" "$4" "$actual_body"
        failed=$((failed + 1))
    else
        echo "PASS $1"
        passed=$((passed + 1))
    fi
}

# check_title NAME RESPONSE STATUS TITLE: passes when RESPONSE has STATUS and its problem details have that title.
check_title() {
    actual_status=$(status_of "$2")
    actual_body=$(body_of "$2")
    if [ "$actual_status" != "$3" ] || [ "$(title_of "$actual_body")" != "$4" ]; then
        printf 'FAIL %s: expected status %s and title %s, got %s %s\n' \
            "$1" "$3" "$4" "$actual_status" "$actual_body"
        failed=$((failed + 1))
    else
        echo "PASS $1"
        passed=$((passed + 1))
    fi
}

response=$(upload "{\"cells\":$vertical_blinker}")
check 'upload the vertical blinker' "$response" 201
blinker_id=$(id_of "$response")

response=$(upload "{\"cells\":$glider}")
check 'upload the glider' "$response" 201
glider_id=$(id_of "$response")

response=$(request "$boards_url/$blinker_id")
expected=$(printf '{"id":"%s","generation":0,"rows":3,"columns":3,"cells":%s}' "$blinker_id" "$vertical_blinker")
check 'fetch the blinker' "$response" 200 "$expected"

response=$(request "$boards_url/$blinker_id/next")
expected=$(printf '{"id":"%s","generation":1,"rows":3,"columns":3,"cells":%s}' "$blinker_id" "$horizontal_blinker")
check 'next generation of the blinker' "$response" 200 "$expected"

response=$(request "$boards_url/$glider_id/generations/4")
expected=$(printf '{"id":"%s","generation":4,"rows":10,"columns":10,"cells":%s}' "$glider_id" "$glider_moved")
check 'glider 4 generations ahead' "$response" 200 "$expected"

response=$(request "$boards_url/$glider_id/final")
expected=$(printf '{"id":"%s","generation":31,"period":1,"rows":10,"columns":10,"cells":%s}' \
    "$glider_id" "$block_in_corner")
check 'final state of the glider' "$response" 200 "$expected"

response=$(upload '{"cells":[[0,1,0],[0,1]]}')
check 'ragged board returns 400' "$response" 400

response=$(request "$boards_url/$glider_id/generations/-1")
check 'generation -1 returns 400' "$response" 400

# A random id: 16 random bytes as 32 hex digits, a GUID written without hyphens, which the API accepts. The title
# is checked too, because a request that matches no route also gets 404.
random_id=$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')
response=$(request "$boards_url/$random_id")
check_title 'unknown id returns 404' "$response" 404 'Board not found'

response=$(upload "@$script_dir/boards/r-pentomino-68.json")
check 'upload the 68 x 68 R-pentomino' "$response" 201
r_pentomino_id=$(id_of "$response")

# On this board the R-pentomino first repeats a generation at 1165, beyond the default limit of 1000.
response=$(request "$boards_url/$r_pentomino_id/final")
check 'final state of the R-pentomino returns 422' "$response" 422

# 1025 KiB of spaces: just over the 1 MiB request body limit.
response=$(dd if=/dev/zero bs=1024 count=1025 2>/dev/null | tr '\0' ' ' | upload @-)
check 'body over 1 MB returns 413' "$response" 413

echo "$passed passed, $failed failed"
if [ "$failed" -gt 0 ]; then
    exit 1
fi
