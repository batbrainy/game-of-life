#!/bin/sh
# Checks that a stored board survives restarts and crashes of the Docker Compose stack. It uploads a board and
# saves its GET body; then, after each step (restart api, kill api, kill db, down and up), it waits for
# /health/ready and requires exactly the same body. It restarts, kills and recreates this project's containers.
# Usage: scripts/verify-restart.sh [base-url]    (base-url defaults to http://localhost:8080; the stack must be up)
# Prints PASS or FAIL for each step and a summary, and exits non-zero when any step fails. If the upload or the
# first fetch fails, it stops there, before touching any container.

base_url=${1:-http://localhost:8080}
passed=0
failed=0

# docker compose looks for compose.yaml in the current directory and its parents, so starting from the repository
# root lets the script run from any directory.
cd "$(dirname "$0")/.." || exit 1

# Prints the response body and then, on a line of its own, the status code (000 when no response arrived). The
# 60-second limit exceeds the API's database timeouts (Npgsql's defaults: 15 seconds to connect, 30 for a command),
# so an answer delayed by the database still arrives.
request() {
    curl --silent --show-error --connect-timeout 2 --max-time 60 --write-out '\n%{http_code}' "$@"
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

# Succeeds once /health/ready returns 200, trying once a second up to 60 times.
wait_until_ready() {
    tries=0
    while [ "$tries" -lt 60 ]; do
        if curl --silent --fail --max-time 5 --output /dev/null "$base_url/health/ready"; then
            return 0
        fi
        tries=$((tries + 1))
        sleep 1
    done
    return 1
}

# Prints the board's GET response as request does. After db is killed and started again, an API that kept running
# can still hold connections to the killed server in its pool: a request that gets one is answered with 503 and the
# connection is dropped. So a 503, and only a 503, is retried, up to 5 tries in all. The other steps start a new
# API process with an empty pool and need no retry, but fetching the same way in every step keeps the steps alike.
fetch_board() {
    response=$(request "$board_url")
    tries=1
    while [ "$(status_of "$response")" = 503 ] && [ "$tries" -lt 5 ]; do
        sleep 1
        response=$(request "$board_url")
        tries=$((tries + 1))
    done
    printf '%s\n' "$response"
}

# check_step NAME EXIT-STATUS: EXIT-STATUS is that of the step's docker compose commands. The step passes when
# they succeeded, the API becomes ready, and the board's GET returns 200 with the saved body.
check_step() {
    if [ "$2" -ne 0 ]; then
        printf 'FAIL %s: docker compose exited with status %s\n' "$1" "$2"
        failed=$((failed + 1))
    elif ! wait_until_ready; then
        printf 'FAIL %s: /health/ready did not return 200 within 60 tries\n' "$1"
        failed=$((failed + 1))
    else
        check "$1" "$(fetch_board)" 200 "$saved_body"
    fi
}

response=$(request --header 'Content-Type: application/json' --data-binary '{"cells":[[0,1,0],[0,0,1],[1,1,1]]}' \
    "$base_url/api/v1/boards")
check 'upload a board' "$response" 201
board_url=$base_url/api/v1/boards/$(id_of "$response")

response=$(request "$board_url")
check 'fetch the board' "$response" 200
saved_body=$(body_of "$response")
if [ "$failed" -gt 0 ]; then
    echo "$passed passed, $failed failed"
    exit 1
fi

docker compose restart api
check_step 'docker compose restart api' "$?"

docker compose kill -s SIGKILL api && docker compose up -d
check_step 'docker compose kill -s SIGKILL api, then docker compose up -d' "$?"

docker compose kill -s SIGKILL db && docker compose up -d
check_step 'docker compose kill -s SIGKILL db, then docker compose up -d' "$?"

docker compose down && docker compose up -d
check_step 'docker compose down, then docker compose up -d' "$?"

echo "$passed passed, $failed failed"
if [ "$failed" -gt 0 ]; then
    exit 1
fi
