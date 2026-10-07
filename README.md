# game-of-life

A REST API for Conway's Game of Life, built with ASP.NET Core minimal APIs on .NET 8 and PostgreSQL 16. A client
uploads a board, a grid of live and dead cells, and gets back an id. With that id it can ask for the board's next
generation, the generation n steps ahead, or its final state: the first state that repeats, which is a still life or an
oscillator. Boards are stored in PostgreSQL, so they survive restarts and crashes of the API and of the database. There
is no authentication.

## Quick start

### Prerequisites

- Docker with Compose v2 (the `docker compose` command). This README was checked with Docker 28.3.0 and Compose
  v2.38.1.
- curl, for the examples below and the scripts in `scripts/`.
- The .NET 8 SDK, only to run the tests or to run the API on the host. [global.json](global.json) selects the SDK.

### Start the stack

```sh
cp .env.example .env
docker compose up --build -d
```

`.env` holds the database name, user and password, the published ports and the ASP.NET Core environment. It is
ignored by git. Compose starts three services in order:

1. `db` runs PostgreSQL 16 and is healthy once `pg_isready` can connect to it.
2. `migrate` runs the API image with the `migrate` command, which applies the SQL migrations and exits 0.
3. `api` runs the API, which is healthy once `GET /health/ready` returns 200. Its health check runs every 5 seconds.

`docker compose up --build -d` returns once `api` has started. Run `docker compose ps -a` until `api` shows
`(healthy)`; the times in the output differ from run to run.

```sh
docker compose ps -a
```

```text
NAME                     IMAGE                COMMAND                  SERVICE   CREATED          STATUS                     PORTS
game-of-life-api-1       game-of-life-api     "dotnet GameOfLife.A…"   api       10 seconds ago   Up 6 seconds (healthy)     127.0.0.1:8080->8080/tcp
game-of-life-db-1        postgres:16-alpine   "docker-entrypoint.s…"   db        10 seconds ago   Up 9 seconds (healthy)     127.0.0.1:5433->5432/tcp
game-of-life-migrate-1   game-of-life-api     "dotnet GameOfLife.A…"   migrate   10 seconds ago   Exited (0) 6 seconds ago
```

The API listens on http://localhost:8080 and PostgreSQL on port 5433. Both are published on 127.0.0.1 only.

### Try the API

Ids and dates differ on every run.

```sh
curl -i http://localhost:8080/health/ready
```

```text
HTTP/1.1 200 OK
Content-Length: 0
Date: Tue, 06 Oct 2026 11:40:01 GMT
Server: Kestrel
Cache-Control: no-store, no-cache
Expires: Thu, 01 Jan 1970 00:00:00 GMT
Pragma: no-cache
```

Upload a board, a vertical blinker, and keep its id in the shell variable `id`. The upload returns `201 Created` with
the body `{"id":"…"}`, and `cut` prints the fourth field of that body split at each double quote, which is the id.

```sh
id=$(curl -s http://localhost:8080/api/v1/boards \
  -H 'Content-Type: application/json' \
  -d '{"cells":[[0,1,0],[0,1,0],[0,1,0]]}' | cut -d '"' -f 4)
echo "$id"
```

```text
69b742d8-7518-4489-abea-921eddf7a151
```

The stored board is generation 0:

```sh
curl "http://localhost:8080/api/v1/boards/$id"
```

```json
{"id":"69b742d8-7518-4489-abea-921eddf7a151","generation":0,"rows":3,"columns":3,"cells":[[0,1,0],[0,1,0],[0,1,0]]}
```

In the next generation the blinker is horizontal:

```sh
curl "http://localhost:8080/api/v1/boards/$id/next"
```

```json
{"id":"69b742d8-7518-4489-abea-921eddf7a151","generation":1,"rows":3,"columns":3,"cells":[[0,0,0],[1,1,1],[0,0,0]]}
```

Two generations ahead it is vertical again:

```sh
curl "http://localhost:8080/api/v1/boards/$id/generations/2"
```

```json
{"id":"69b742d8-7518-4489-abea-921eddf7a151","generation":2,"rows":3,"columns":3,"cells":[[0,1,0],[0,1,0],[0,1,0]]}
```

Its final state is the cycle that starts at generation 0 and repeats every 2 generations, so the blinker is an
oscillator:

```sh
curl "http://localhost:8080/api/v1/boards/$id/final"
```

```json
{"id":"69b742d8-7518-4489-abea-921eddf7a151","generation":0,"period":2,"rows":3,"columns":3,"cells":[[0,1,0],[0,1,0],[0,1,0]]}
```

The liveness probe:

```sh
curl http://localhost:8080/health/live
```

```text
Healthy
```

### Swagger UI

Swagger UI is at http://localhost:8080/swagger and the OpenAPI document at
http://localhost:8080/swagger/v1/swagger.json. The API serves both only in the Development environment, which `.env`
sets with `ASPNETCORE_ENVIRONMENT=Development`. In any other environment both return 404.

### After changing code

```sh
docker compose build
docker compose up -d
```

`docker compose up -d` recreates `migrate` and `api` from the new image, and `migrate` applies any new migration before
`api` starts. Build first instead of running `docker compose up --build -d`: Compose v2.38.1, which builds with bake,
can build the new image in that command and still leave `migrate` and `api` on the old one.
`docker compose up --build -d --force-recreate` also runs the new image.

### Stop the stack

```sh
docker compose down
```

This removes the containers and keeps the boards in the volume `game-of-life_pgdata`. The next `docker compose up -d`
serves them again.

```sh
docker compose down -v
```

This also deletes the volume, and with it every board.

## API reference

The examples in this section need the stack running. If you stopped it, start it again; with `--wait`, the command
returns once `api` is healthy:

```sh
docker compose up -d --wait
```

| Method and path | Returns | Success | Errors |
|---|---|---|---|
| `POST /api/v1/boards` | Stores the board in the body | 201, `Location`, `{"id"}` | 400, 413, 415, 503 |
| `GET /api/v1/boards/{id}` | The stored board, generation 0 | 200 | 400, 404, 503 |
| `GET /api/v1/boards/{id}/next` | Generation 1 | 200 | 400, 404, 503 |
| `GET /api/v1/boards/{id}/generations/{n}` | Generation `n`, from 0 to `MaxGenerationsAhead` | 200 | 400, 404, 503 |
| `GET /api/v1/boards/{id}/final` | The final state | 200 | 400, 404, 422, 503 |
| `GET /health/live` | The process is serving requests | 200 | |
| `GET /health/ready` | The database answers and every migration is applied | 200 | 503 |

Any request can also get 500 for an unexpected failure. The board endpoints return 503 "Database unavailable" when the
database cannot be reached. `/next`, `/generations/{n}` and `/final` also return 503 "Server busy" with
`Retry-After: 1` while `MaxConcurrentSimulations` requests to them are in progress. That bound is checked before the id
and `n` are read, so while it is reached a malformed request to those three endpoints also gets 503 instead of 400.

The OpenAPI document does not list the 413, 415 and 500 responses, or the 503 of upload and fetch.

### Upload a board

`POST /api/v1/boards` takes a JSON body sent with `Content-Type: application/json`:

```json
{ "cells": [[0,1,0],[0,1,0],[0,1,0]] }
```

`cells` holds the rows from top to bottom, and each row holds its cells from left to right: 0 for dead and 1 for
alive. A board has 1 to `MaxRows` rows (256 by default), and every row has the same number of cells, 1 to `MaxColumns`
(256 by default). A cell must be the JSON number 0 or 1: `"1"`, `true` and `1.5` are rejected.

```sh
curl -i http://localhost:8080/api/v1/boards \
  -H 'Content-Type: application/json' \
  -d '{"cells":[[0,1,0],[0,1,0],[0,1,0]]}'
```

```text
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Date: Tue, 06 Oct 2026 11:40:29 GMT
Server: Kestrel
Location: /api/v1/boards/3a44ccc1-3286-4c70-a83f-f72acf056d5a
Transfer-Encoding: chunked

{"id":"3a44ccc1-3286-4c70-a83f-f72acf056d5a"}
```

### Board responses

The fetch, `/next`, `/generations/{n}` and `/final` return JSON objects with these members:

| Member | Meaning |
|---|---|
| `id` | The board's id |
| `generation` | 0 for the fetch, 1 for `/next` and `n` for `/generations/{n}`; for `/final`, the first generation of the cycle |
| `period` | Only in `/final`: the length of the cycle, 1 for a still life and more than 1 for an oscillator |
| `rows`, `columns` | The board's size, which is always the uploaded size |
| `cells` | The rows of cells, as in the upload |

A live cell with 2 or 3 live neighbours stays alive, a dead cell with exactly 3 becomes alive, and every other cell is
dead. Cells outside the grid count as dead, and the board keeps its size.

`/final` computes generations until one repeats an earlier one, up to `MaxFinalStateGenerations` (500 by default), and
returns 422 when none does. The repeat must happen within the limit, so a board whose cycle starts at generation `g`
with period `p` needs a limit of at least `g + p`.

### Errors

Errors are RFC 9457 problem details (`application/problem+json`) with `type`, `title` and `status`, and `detail` or
`errors` when there is more to say. A validation error has an `errors` member that maps a field path to messages. The
upload is checked in a fixed order and only the first problem is reported.

A board with a short row:

```sh
curl http://localhost:8080/api/v1/boards \
  -H 'Content-Type: application/json' \
  -d '{"cells":[[0,1,0],[0,1]]}'
```

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"cells[1]":["Every row must have as many cells as the first row, which has 3; this one has 2."]}}
```

An `n` that is a 32-bit integer but outside the allowed range, 0 to `MaxGenerationsAhead`. It is checked before the
board is looked up, so this id needs no board:

```sh
curl http://localhost:8080/api/v1/boards/00000000-0000-0000-0000-000000000000/generations/501
```

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"n":["The generation must be 0 to 500; this request asks for 501."]}}
```

ASP.NET Core writes the 400 itself, with no `detail` or `errors`, for a body that cannot be read as an upload, such as
malformed JSON or a cell written as `"1"`, for an id that is not a GUID, and for an `n` that is not a 32-bit integer,
such as `abc`, `1.5` or `2147483648`. An id that is not a GUID:

```sh
curl http://localhost:8080/api/v1/boards/not-a-guid
```

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400}
```

An id with no board:

```sh
curl http://localhost:8080/api/v1/boards/00000000-0000-0000-0000-000000000000
```

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Board not found","status":404,"detail":"No board is stored under the id 00000000-0000-0000-0000-000000000000."}
```

A board that does not repeat a generation within `MaxFinalStateGenerations`. The R-pentomino in
[scripts/boards/r-pentomino-68.json](scripts/boards/r-pentomino-68.json) first repeats one at generation 1165:

```sh
pentomino_id=$(curl -s http://localhost:8080/api/v1/boards \
  -H 'Content-Type: application/json' \
  --data-binary @scripts/boards/r-pentomino-68.json | cut -d '"' -f 4)
curl "http://localhost:8080/api/v1/boards/$pentomino_id/final"
```

```json
{"type":"https://tools.ietf.org/html/rfc4918#section-11.2","title":"Board did not reach a final state","status":422,"detail":"The board did not repeat an earlier generation within the limit of 500 generations.","maxGenerations":500}
```

A body larger than 1 MiB (1,048,576 bytes). Unlike the other errors here, this problem details document has no
`type`:

```sh
head -c 1048577 /dev/zero | tr '\0' ' ' | curl -s http://localhost:8080/api/v1/boards \
  -H 'Content-Type: application/json' \
  --data-binary @-
```

```json
{"title":"Payload Too Large","status":413}
```

An upload that is not JSON:

```sh
curl http://localhost:8080/api/v1/boards \
  -H 'Content-Type: text/plain' \
  -d '{"cells":[[0,1,0],[0,1,0],[0,1,0]]}'
```

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.16","title":"Unsupported Media Type","status":415}
```

A request to `/next`, `/generations/{n}` or `/final` while `MaxConcurrentSimulations` requests to them are in progress.
[scripts/measure-worst-case.sh](scripts/measure-worst-case.sh) causes this response and checks its status and
`Retry-After` header:

```text
HTTP/1.1 503 Service Unavailable
Content-Type: application/problem+json
Date: Tue, 06 Oct 2026 11:10:25 GMT
Server: Kestrel
Retry-After: 1
Transfer-Encoding: chunked

{"type":"https://tools.ietf.org/html/rfc9110#section-15.6.4","title":"Server busy","status":503,"detail":"The server is handling as many simulation requests as it allows. Try again shortly."}
```

A request while the database is down. Stop the database:

```sh
docker compose stop db
```

Board requests now get 503:

```sh
curl http://localhost:8080/api/v1/boards/00000000-0000-0000-0000-000000000000
```

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.6.4","title":"Database unavailable","status":503,"detail":"The database is temporarily unavailable. Try again later."}
```

The readiness probe fails too, while the liveness probe still returns 200:

```sh
curl http://localhost:8080/health/ready
```

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.6.4","title":"Service Unavailable","status":503}
```

Start the database again:

```sh
docker compose start db
```

An unexpected failure gets a 500 that does not reveal the exception:

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.6.1","title":"An error occurred while processing your request.","status":500}
```

Problem details are JSON. A client whose `Accept` header rules out JSON, such as `Accept: text/html`, gets:

- problem details, as above, for the validation errors, "Board not found" and the 422, because the endpoints write
  them.
- plain text from `UseStatusCodePages`, such as `Status Code: 400; Bad Request`, for the 400s that ASP.NET Core writes
  itself, the 404 for an unknown route, the 405 for a method a route does not allow, the 413, the 415 and the 503 from
  `/health/ready`.
- `Status Code: 503; Service Unavailable` as plain text, with `Retry-After: 1`, for a request beyond the concurrency
  bound.
- an empty body with a 500, and with the 503 for an unreachable database.

Kestrel, the web server, answers some malformed requests itself, before they reach the API, with an empty body whatever
the `Accept` header says: for example a 400 for a header line without a colon, and a 431 for request headers that are
too large.

### Limits

| Setting | Default | What it limits |
|---|---|---|
| `MaxRows` | 256 | Rows in an uploaded board |
| `MaxColumns` | 256 | Cells in each row |
| `MaxGenerationsAhead` | 500 | The largest `n` for `/generations/{n}` |
| `MaxFinalStateGenerations` | 500 | The generations `/final` computes before it returns 422 |
| `MaxConcurrentSimulations` | The number of processors | Requests to `/next`, `/generations/{n}` and `/final` handled at once |

The settings belong to the configuration section `GameOfLife`, and the API checks them at startup: each one must be a
whole number from 1 to 2,147,483,647. The request body limit of 1 MiB is fixed in `Program.cs`.

In the Compose stack, set them on `api` in a file named `compose.override.yaml` next to `compose.yaml`. Compose reads it
together with `compose.yaml` without any extra option. For example, to allow `n` up to 1000, create
`compose.override.yaml` with:

```yaml
services:
  api:
    environment:
      GameOfLife__MaxGenerationsAhead: "1000"
```

Then recreate `api` and wait until it is healthy:

```sh
docker compose up -d --wait
```

The 400 for an `n` out of range now states the new limit:

```sh
curl http://localhost:8080/api/v1/boards/00000000-0000-0000-0000-000000000000/generations/-1
```

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"n":["The generation must be 0 to 1000; this request asks for -1."]}}
```

To go back to the defaults, delete the file and recreate `api`:

```sh
rm compose.override.yaml
docker compose up -d --wait
```

When the API runs on the host, set the same names as environment variables, for example
`GameOfLife__MaxGenerationsAhead=1000`.

An invalid value stops the API at startup. With `GameOfLife__MaxGenerationsAhead: "0"` in `compose.override.yaml`,
`docker compose ps -a` shows `api` as `Restarting`, and `docker compose logs api` includes:

```text
Microsoft.Extensions.Options.OptionsValidationException: DataAnnotation validation failed for 'GameOfLifeOptions' members: 'MaxGenerationsAhead' with the error: 'The field MaxGenerationsAhead must be between 1 and 2147483647.'.
```

A value that is not a 32-bit integer, such as `"abc"` or `"3000000000"`, fails with
`Failed to convert configuration value at 'GameOfLife:MaxGenerationsAhead' to type 'System.Int32'.`

### Health endpoints

`GET /health/live` runs no checks: it returns 200 with the text `Healthy` while the process serves requests.
`GET /health/ready` returns 200 with an empty body when the database answers a query and its `schema_migrations` table
records every migration built into the API. It returns 503 problem details when the database cannot be reached or a
migration is missing. The `api` container's health check calls `/health/ready` with `wget -q --spider`, because the
image has BusyBox `wget` and no `curl`.

## Running the tests

Run the tests from the repository root while Docker is running:

```sh
dotnet test
```

The Api tests start a PostgreSQL 16 container with Testcontainers and create their databases in it; they do not use
the Compose stack. Run them in a shell where `ConnectionStrings__GameOfLife` is not set, because one test checks that
`migrate` fails without a connection string.

| Project | Covers |
|---|---|
| `tests/GameOfLife.Core.Tests` | The board and the rules, with no external dependencies: creating and comparing boards, the next generation for every neighbour count and at the edges, advancing n generations, and the final-state search, which is also compared with a brute-force search on 200 random boards |
| `tests/GameOfLife.Api.Tests` | The API over HTTP against PostgreSQL: upload validation and limits, the exact responses of the board endpoints, the concurrency bound, problem details, the 503 and 500 for database failures, the health endpoints, the OpenAPI document and Swagger UI, the migration runner, the `migrate` command, the `boards` table's constraints and the repository |

Three scripts check the running Compose stack end to end. Each takes the API's base URL as its first argument, which
defaults to http://localhost:8080, needs curl, prints a line for each check and exits non-zero when a check fails.

| Script | Checks | Also needs |
|---|---|---|
| [scripts/smoke-test.sh](scripts/smoke-test.sh) | Uploads boards and compares the fetch, `/next`, `/generations/4` and `/final` responses with the exact JSON expected, then checks the 400 for a short row and for n = -1, the 404 for an unknown id, the 422 for the R-pentomino board and the 413 for a body over 1 MiB | Nothing else |
| [scripts/verify-restart.sh](scripts/verify-restart.sh) | Uploads a board, then checks that it comes back unchanged after `docker compose restart api`, after `api` is killed, after `db` is killed, and after `docker compose down` and `up -d`. It restarts, kills and recreates the stack's containers | `docker compose` |
| [scripts/measure-worst-case.sh](scripts/measure-worst-case.sh) | Times the slowest requests that the limits allow, and checks that a request beyond the concurrency bound gets 503 with `Retry-After: 1`. See [Measured worst-case timings](#measured-worst-case-timings) | `docker compose`, unless its second argument gives `MaxConcurrentSimulations` |

```sh
scripts/smoke-test.sh
```

```text
PASS upload the vertical blinker
PASS upload the glider
PASS fetch the blinker
PASS next generation of the blinker
PASS glider 4 generations ahead
PASS final state of the glider
PASS ragged board returns 400
PASS generation -1 returns 400
PASS unknown id returns 404
PASS upload the 68 x 68 R-pentomino
PASS final state of the R-pentomino returns 422
PASS body over 1 MB returns 413
12 passed, 0 failed
```

```sh
scripts/verify-restart.sh
```

## Running the API on the host

To run the API from source, for example in a debugger, use the Compose database. Start only `db` and wait until it is
healthy, apply the migrations, then start the API. The connection string holds the default values from `.env.example`;
change them if your `.env` differs:

```sh
docker compose up -d --wait db
ConnectionStrings__GameOfLife='Host=localhost;Port=5433;Database=gameoflife;Username=gameoflife;Password=local-development-only' \
  dotnet run --project src/GameOfLife.Api -- migrate
ConnectionStrings__GameOfLife='Host=localhost;Port=5433;Database=gameoflife;Username=gameoflife;Password=local-development-only' \
  dotnet run --project src/GameOfLife.Api
```

`dotnet run` uses the `http` profile in
[src/GameOfLife.Api/Properties/launchSettings.json](src/GameOfLife.Api/Properties/launchSettings.json): the API serves
http://localhost:5055 in the Development environment, with Swagger UI at http://localhost:5055/swagger. Stop it with
Ctrl+C. Each command sets the connection string for itself instead of exporting it, so that it stays out of the shell
that runs `dotnet test`.

[src/GameOfLife.Api/GameOfLife.Api.http](src/GameOfLife.Api/GameOfLife.Api.http) has a request for every endpoint at
http://localhost:5055, for editors that run `.http` files. Set `@boardId` to an id that an upload returned.

## Adding a migration

1. Add a script named `NNNN_lower_snake_case.sql` to
   [src/GameOfLife.Api/Persistence/Migrations/Scripts](src/GameOfLife.Api/Persistence/Migrations/Scripts), with the
   next number, for example `0002_add_board_label.sql`. The project file embeds every `.sql` file in that folder in the
   API assembly, so no other file changes. A file name that does not match the pattern makes `migrate` fail.
2. Apply it with `docker compose build` and then `docker compose up -d`, as after any code change, or on the host with
   the `migrate` command above.

`migrate` first takes a PostgreSQL advisory lock, so runs that overlap wait for each other. It then applies every
script whose version is not yet in `schema_migrations`, in version order and in one transaction: if one script fails,
none is applied. It records each script's version, name and time in `schema_migrations`. `/health/ready` returns 503
until every migration built into the API is recorded, so an API with a new script reports ready only after `migrate`
has applied it.

Never edit a script that has been applied anywhere. Nothing checksums applied scripts: `migrate` skips every version
that is already recorded, so the edited script never runs on that database and nothing reports the difference. Add a
new script instead.

## Design decisions

[docs/implementation-plan.md](docs/implementation-plan.md) has the full plan, including the API contract and the order
of work. Each decision below gives the choice and its reason, what was rejected, and the choice's main limitation.

1. Read model. A stored board never changes, and the next generation, n generations ahead and the final state are
   `GET` requests computed from the upload, so reads are safe to repeat and no two callers can interfere. Rejected:
   saving each computed generation as the board's new state, which needs `POST` and optimistic concurrency. Limitation:
   every request starts again from the upload, so `/generations/500` always computes 500 generations.
2. Grid edges. A board keeps its uploaded rows and columns, and cells outside the grid are dead, so responses keep the
   uploaded shape, the work per request is bounded, and every board ends in a still life or a cycle. Rejected:
   wrap-around edges and an unbounded plane. Limitation: patterns are cut off at the edges; the glider in
   `scripts/smoke-test.sh` becomes a block in the bottom-right corner at generation 31 instead of travelling on.
3. Final state. The final state is the first state that repeats. Period 1 is a still life and a longer period an
   oscillator; both return 200, because a cycle is a settled outcome, and the response says which kind it is. Rejected:
   treating oscillators as errors. Limitation: a board that first repeats a generation after
   `MaxFinalStateGenerations` gets 422 although it settles later, like the R-pentomino board, which first repeats a
   generation at 1165.
4. Database access. PostgreSQL through Npgsql, with hand-written parameterized SQL: one table and two statements, and
   nothing hidden behind a mapper. Rejected: an ORM, and an embedded file database. Limitation: the SQL is written by
   hand in strings, so only the tests show that it still matches the schema.
5. Schema changes. Numbered SQL scripts applied by a `migrate` command that finishes before the API starts, and the API
   runs no DDL, so a schema change is an explicit step and the API could run under a role without schema rights.
   Rejected: migrating inside API startup. Limitation: scripts only go forward, as there are no down scripts, and in
   this stack `migrate` and the API still use the same superuser role.
6. Projects. `GameOfLife.Core`, with no package references, holds the board and the rules, and `GameOfLife.Api` holds
   everything else, so the compiler keeps the simulation code free of HTTP and database dependencies. Rejected: one
   project, and a three-layer split. Limitation: inside `GameOfLife.Api`, endpoints, persistence and migrations are
   kept apart by folders only.
7. HTTP style. Minimal APIs, which are the default of the .NET 8 `webapi` template and fit five board endpoints, with
   all five in one route group. Rejected: MVC controllers. Limitation: minimal APIs in .NET 8 have no built-in request
   validation (it arrived in ASP.NET Core 10), so the upload's checks are written by hand in `BoardRequestValidator`.
8. Cycle detection. `/final` remembers every generation in a dictionary keyed by board, and the first repeat gives the
   cycle's start and period exactly. It is the simplest exact method. Rejected: Brent's algorithm, which needs constant
   memory but extra simulation steps, and is the one to revisit if the limits are raised. Limitation: memory grows
   with the limit; at the defaults one request holds up to 501 x 65,536 bytes = 32.8 MB of cells, and 10 requests at
   once up to 328 MB.
9. Cell storage. One `bool` per cell, row by row: a `bool[]` in memory and a `boolean[]` column with `CHECK`
   constraints on the dimensions and the array length. No code reads or writes single bits, Npgsql maps one to the
   other directly, and the database rejects malformed rows. Rejected: bit-packed `bytea`, which is 8 times smaller but
   needs bit manipulation, and a JSON or text column. Limitation: each cell takes a byte where a bit would do, in
   memory and in the database.
10. Bad `id` or `n`. Both are bound as typed parameters, a GUID and an `int`, and a malformed value returns 400, because
    the ASP.NET Core routing documentation says route constraints are not for input validation. Rejected: route
    constraints, which return 404. Limitation: ASP.NET Core writes that 400 itself, with no `detail` or `errors` that
    names the parameter.
11. Errors. Every error is an RFC 9457 problem details document, so clients handle one error shape. Rejected: ad hoc
    error bodies. Limitation: the 413 has no `type`, a client whose `Accept` header rules out JSON gets some errors as
    plain text or with an empty body instead of problem details, and Kestrel answers some malformed requests itself
    with an empty body; see [Errors](#errors).
12. Board ids. Random GUIDs generated by the API, so the id is known before the insert. Rejected: ids generated by the
    database. Limitation: an upload retried after a lost response stores a second board under a new id.
13. Local configuration. A `.env` file, ignored by git and copied from `.env.example`, so credentials set in it stay out
    of version control. `.env.example` and the host commands in
    [Running the API on the host](#running-the-api-on-the-host) hold only local development values. Rejected: defaults
    committed in `compose.yaml`. Limitation: Compose does not start until `.env` exists; it stops with an error such as
    `required variable POSTGRES_DB is missing a value: copy .env.example to .env`.
14. Where simulations run. Inside the request, under the hard limits and a concurrency bound: at most
    `MaxConcurrentSimulations` run at once, and a request beyond the bound gets 503 instead of waiting for a permit. One
    call returns the result, with no job store, worker or polling, and the bound turns extra load away instead of
    letting it slow every request. Rejected: background jobs now. Limitation: at the default limits a request can
    take more than a second (see [Measured worst-case timings](#measured-worst-case-timings)), and larger work is
    refused, not deferred.

Durable background jobs, with a job id that the client polls, become the right design when measured workloads
outgrow a request, for example when limits that users need make requests take longer than clients will wait, or when
work must continue after the client disconnects. Today a simulation stops when its client disconnects, because it
checks the request's cancellation before every generation.

## Measured worst-case timings

The slowest requests the default limits allow are `GET /generations/500` and `GET /final` on a 256 x 256 board. Each
generation looks at all eight neighbours of every cell, alive or dead, so its cost depends only on the board's size.
The random board that `scripts/measure-worst-case.sh` uploads does not repeat within 500 generations, so `/final`
computes all 500.

[docs/implementation-plan.md](docs/implementation-plan.md) records these medians, measured on an Apple M1 Max with 10
cores and 64 GiB, whose Docker VM had 10 CPUs and 7.7 GiB, so `MaxConcurrentSimulations` was 10:

| `MaxGenerationsAhead` and `MaxFinalStateGenerations` | Requests | Medians |
|---|---|---|
| 1000 | One at a time | 1.29 to 1.32 s |
| 500, the default | One at a time | 0.67 to 0.76 s |
| 500, the default | 10 at once | 0.91 to 1.32 s |

At 1000 a request on its own took more than a second, so both limits are 500.

While 10 simulations run, other requests still get a thread straight away, because the API raises the thread pool's
minimum number of worker threads at startup to the processor count plus `MaxConcurrentSimulations`. Without that, a new
request could wait for the pool to add a thread: in three runs on 2026-10-06 the 503 for a request beyond the bound
took up to 0.87 s and `/health/live` up to 0.72 s. With it, in three runs on the same day, the 503 took at most 0.097 s
and `/health/live` at most 0.034 s.

A run on the same machine on 2026-10-06, made while checking this README, gave medians of 0.688 s for
`/generations/500` and 0.684 s for `/final` one at a time, and 1.064 s and 0.968 s with 10 at once.

To reproduce the measurements, start the stack. If it is already running, build and start it as in
[After changing code](#after-changing-code) instead, so that the script measures the current code. The first line below
copies `.env.example` to `.env` only when `.env` does not exist yet, so a `.env` that you edited is kept.

```sh
[ -f .env ] || cp .env.example .env
docker compose up --build -d
```

When `docker compose ps -a` shows `api` as `(healthy)`, run the script:

```sh
scripts/measure-worst-case.sh
```

The script prints each request's status and time, then the minimum, median and maximum time for each endpoint. Times
vary from run to run.

## Known limitations

- No per-client rate limiting and no request timeout. The concurrency bound is for the whole server, and only the
  simulation limits bound how long a request computes.
- One database role. `migrate` and the API connect as the same user, the superuser that the postgres image creates from
  `POSTGRES_USER`, so the API could change the schema although it runs no DDL.
- No board deletion or retention. Boards stay until the volume is deleted, and there is no `DELETE` route; a `DELETE`
  request gets 405.
- Uploads are not idempotent. Every upload stores a new board under a new id, so an upload sent twice is stored twice.
- Applied migrations are not checksummed, so an edit to an applied script goes unnoticed; see
  [Adding a migration](#adding-a-migration).
- No TLS. The container serves plain HTTP on port 8080, published on 127.0.0.1 only.
- Computed generations are not cached. Every request computes from the uploaded board.
- Right after the database restarts, the first requests can get 503. The API's connection pool still holds
  connections to the old server process, and a request that gets one fails with 503 while the broken connection is
  dropped. `scripts/verify-restart.sh` retries a 503 for this reason.
- A connection string that Npgsql cannot parse, such as one with an unknown keyword, makes the board endpoints and
  `/health/ready` return 500 instead of 503. The API still starts, and `/health/live` returns 200.
- The 413 problem details document has no `type` member.
- A database that accepts connections but does not answer keeps the simulation permits held. With the database container
  paused, each request that needed the database got 503 after 15 s, or after 45 s if it reused an open connection (60 s
  with the API on the host). While `MaxConcurrentSimulations` requests to `/next`, `/generations/{n}` and `/final`
  waited like this, further requests to them got 503 "Server busy" at once instead of "Database unavailable".
- .NET 8 reaches end of support on 10 November 2026, after which Microsoft no longer provides fixes or updates for it.
- NuGet audit warnings are build errors, because `TreatWarningsAsErrors` is on. An advisory published later for a
  package that a project references directly fails `dotnet restore`, and with it the build, until the package is updated
  or the advisory is suppressed with `NuGetAuditSuppress`. Advisories for packages that are referenced only indirectly,
  through other packages, do not fail it.

## License

[MIT](LICENSE)
