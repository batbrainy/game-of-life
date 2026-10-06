#!/bin/sh
# Times the slowest requests that the API's limits allow. It uploads a 256 x 256 board of random cells and times
# GET .../generations/{MaxGenerationsAhead} and GET .../final, five requests each, one after another. Then it sends
# MaxConcurrentSimulations requests at once to each of the two, and checks that an extra request made while they run
# is answered with 503 and Retry-After: 1.
# Usage: scripts/measure-worst-case.sh [base-url] [max-concurrent-simulations]
#   base-url defaults to http://localhost:8080. max-concurrent-simulations defaults to the number of processors in the
#   Compose stack's api container, from docker compose exec -T api nproc, which is the API's default bound.
# Prints each request's status code and time and the minimum, median and maximum time per endpoint, and exits
# non-zero when a request gets an unexpected status or, for either endpoint, no extra request gets 503 in two attempts.
#
# Why these are the slowest requests: Board.Next() looks at all eight neighbours of every cell, alive or dead, so the
# work for /generations/n depends only on the board's size and n, and both are at their limits here. /final stops at
# the first generation that repeats an earlier one; this random board does not repeat within the limit, as the 422
# shows, so /final runs the whole limit.

# sort -n and awk read numbers by the locale's rules, so in a locale with a decimal comma they would misread the times
# that curl writes with a decimal point.
export LC_ALL=C

base_url=${1:-http://localhost:8080}
boards_url=$base_url/api/v1/boards
failed=0

if [ -n "$2" ]; then
    max_concurrent_simulations=$2
    bound_source='from the command line'
else
    # docker compose looks for compose.yaml in the current directory and its parents, so it runs from the
    # repository root.
    max_concurrent_simulations=$(cd "$(dirname "$0")/.." && docker compose exec -T api nproc) || exit 1
    bound_source='from docker compose exec -T api nproc'
fi

# Holds the responses and the other files the measurements write, and is removed when the script exits.
work_dir=$(mktemp -d) || exit 1
trap 'rm -rf "$work_dir"' EXIT

# Prints the response body and then, on a line of its own, the status code (000 when no response arrived) and the
# time the request took in seconds. The 60-second limit exceeds the API's database timeouts (Npgsql's defaults: 15
# seconds to connect, 30 for a command), so an answer delayed by the database still arrives.
request() {
    curl --silent --show-error --connect-timeout 2 --max-time 60 --write-out '\n%{http_code} %{time_total}' "$@"
}

# Uploads $1, which curl sends as the body: JSON text, or @- for standard input.
upload() {
    request --header 'Content-Type: application/json' --data-binary "$1" "$boards_url"
}

status_of() {
    printf '%s\n' "$1" | tail -n 1 | cut -d ' ' -f 1
}

time_of() {
    printf '%s\n' "$1" | tail -n 1 | cut -d ' ' -f 2
}

body_of() {
    printf '%s\n' "$1" | sed '$d'
}

id_of() {
    printf '%s\n' "$1" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p'
}

# require_status NAME RESPONSE STATUS: stops the script when RESPONSE does not have STATUS, because the measurements
# need what that request sets up.
require_status() {
    if [ "$(status_of "$2")" != "$3" ]; then
        printf 'FAIL %s: expected status %s, got %s %s\n' "$1" "$3" "$(status_of "$2")" "$(body_of "$2")"
        exit 1
    fi
}

# Prints the upload body for a board of 256 x 256 random cells, the default MaxRows x MaxColumns. The numbers come
# from the Park-Miller generator, x = x * 16807 % 2147483647, started from a fixed seed, so every run uploads the same
# board. Each product stays below 2^53, and awk's numbers hold every whole number that small exactly, so every awk
# computes the same numbers. A cell is alive when its number is in the lower half of the range.
random_board() {
    awk 'BEGIN {
        x = 42
        printf "{\"cells\":["
        for (row = 0; row < 256; row++) {
            if (row > 0) {
                printf ","
            }
            printf "["
            for (column = 0; column < 256; column++) {
                if (column > 0) {
                    printf ","
                }
                x = x * 16807 % 2147483647
                if (x < 1073741824) {
                    printf "1"
                } else {
                    printf "0"
                }
            }
            printf "]"
        }
        printf "]}"
    }'
}

# report_responses COUNT STATUS: prints the status code and time of response.1 to response.COUNT, counting a failure
# for each one without STATUS, and then the minimum, median and maximum time. The median of an even count of times is
# the mean of the two middle ones.
report_responses() {
    : > "$work_dir/times"
    number=1
    while [ "$number" -le "$1" ]; do
        response=$(cat "$work_dir/response.$number")
        if [ "$(status_of "$response")" = "$2" ]; then
            printf '  request %s: %s in %s s\n' "$number" "$(status_of "$response")" "$(time_of "$response")"
        else
            printf '  FAIL request %s: expected status %s, got %s %s\n' \
                "$number" "$2" "$(status_of "$response")" "$(body_of "$response")"
            failed=$((failed + 1))
        fi
        time_of "$response" >> "$work_dir/times"
        number=$((number + 1))
    done
    sort -n "$work_dir/times" | awk '
        { seconds[NR] = $1 }
        END {
            if (NR % 2 == 1) {
                median = seconds[(NR + 1) / 2]
            } else {
                median = (seconds[NR / 2] + seconds[NR / 2 + 1]) / 2
            }
            printf "  min %.3f s, median %.3f s, max %.3f s\n", seconds[1], median, seconds[NR]
        }'
}

# time_one_at_a_time URL STATUS: requests URL five times, each request after the one before has finished, and
# reports the responses.
time_one_at_a_time() {
    rm -f "$work_dir"/response.*
    number=1
    while [ "$number" -le 5 ]; do
        request "$1" > "$work_dir/response.$number"
        number=$((number + 1))
    done
    report_responses 5 "$2"
}

# time_at_once URL STATUS: starts max_concurrent_simulations requests for URL at once, in the background, sends extra
# requests while they run, and reports the responses once all of them have finished. Each background request creates
# done.N when it has finished. A batch can end with no extra request turned away even though the bound works (see
# send_extra_requests), so a failure counts only when a second batch ends that way too.
time_at_once() {
    for attempt in 1 2; do
        rm -f "$work_dir"/response.* "$work_dir"/done.* "$work_dir"/extra.*
        number=1
        while [ "$number" -le "$max_concurrent_simulations" ]; do
            { request "$1" > "$work_dir/response.$number"; : > "$work_dir/done.$number"; } &
            number=$((number + 1))
        done
        send_extra_requests
        report_responses "$max_concurrent_simulations" "$2"
        response=$(turned_away_response)
        if [ -n "$response" ]; then
            echo "  extra request while they run: 503 with Retry-After: 1, answered in $(time_of "$response") s"
            return
        fi
        echo "  extra requests while they run: none was answered with 503 and Retry-After: 1 (attempt $attempt of 2)"
    done
    echo '  FAIL no extra request was answered with 503 and Retry-After: 1 in two attempts'
    failed=$((failed + 1))
}

# Prints how many of the requests started by time_at_once have finished.
finished_count() {
    count=0
    for marker in "$work_dir"/done.*; do
        if [ -e "$marker" ]; then
            count=$((count + 1))
        fi
    done
    echo "$count"
}

# Prints the response of the first extra request that was answered with 503 and Retry-After: 1, or nothing. Header
# lines end in a carriage return and a line feed, so tr removes the carriage return before grep -x matches the whole
# line.
turned_away_response() {
    number=1
    while [ -e "$work_dir/extra.$number" ]; do
        response=$(cat "$work_dir/extra.$number")
        if [ "$(status_of "$response")" = 503 ] &&
            printf '%s\n' "$response" | tr -d '\r' | grep -qx 'Retry-After: 1'; then
            printf '%s\n' "$response"
            return
        fi
        number=$((number + 1))
    done
}

# Sends GET /next for a small board every tenth of a second, each request in the background, until one has been
# answered with 503 and Retry-After: 1 or every timed request has finished, and then waits for every request to
# finish. The first one waits too, so that the timed requests have taken every permit before it arrives; sent sooner,
# it could take a permit and leave a timed request to be turned away. They go in the background because an answer can
# be late: while the timed requests keep the API's thread-pool threads busy, a new request waits for a free one before
# the bound is checked, and one that gets a thread only when a timed request finishes can find a free permit and get
# 200. So a batch can end with no extra request turned away even though the bound works.
send_extra_requests() {
    extra=0
    while [ "$(finished_count)" -lt "$max_concurrent_simulations" ] && [ -z "$(turned_away_response)" ]; do
        sleep 0.1
        extra=$((extra + 1))
        request --include "$extra_request_url" > "$work_dir/extra.$extra" &
    done
    wait
}

response=$(random_board | upload @-)
require_status 'upload the 256 x 256 board' "$response" 201
board_url=$boards_url/$(id_of "$response")

response=$(upload '{"cells":[[0,1,0],[0,1,0],[0,1,0]]}')
require_status 'upload the board for the extra requests' "$response" 201
extra_request_url=$boards_url/$(id_of "$response")/next

# The 400 for n = -1 states the allowed range: "The generation must be 0 to {MaxGenerationsAhead}; ...".
response=$(request "$board_url/generations/-1")
max_generations_ahead=$(body_of "$response" | sed -n 's/.*must be 0 to \([0-9]*\);.*/\1/p')
if [ -z "$max_generations_ahead" ]; then
    printf 'FAIL read MaxGenerationsAhead: expected a 400 that states the range of n, got %s %s\n' \
        "$(status_of "$response")" "$(body_of "$response")"
    exit 1
fi

echo "Board: 256 x 256 random cells, $board_url"
echo "MaxGenerationsAhead: $max_generations_ahead, from the 400 for n = -1"
echo "MaxConcurrentSimulations: $max_concurrent_simulations, $bound_source"

echo "GET /generations/$max_generations_ahead, 5 requests one after another"
time_one_at_a_time "$board_url/generations/$max_generations_ahead" 200

echo "GET /final, 5 requests one after another"
time_one_at_a_time "$board_url/final" 422
max_final_state_generations=$(sed -n 's/.*"maxGenerations":\([0-9]*\).*/\1/p' "$work_dir/response.1")
echo "  MaxFinalStateGenerations: $max_final_state_generations, from the 422"

echo "GET /generations/$max_generations_ahead, $max_concurrent_simulations requests at once"
time_at_once "$board_url/generations/$max_generations_ahead" 200

echo "GET /final, $max_concurrent_simulations requests at once"
time_at_once "$board_url/final" 422

echo "$failed failed"
if [ "$failed" -gt 0 ]; then
    exit 1
fi
