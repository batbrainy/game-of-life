# Implementation plan

Status: complete. This replaces the immutable-upload design and builds on commit `d27b454`, including its thread-pool improvement. The source, README, tests, and scripts implement the contract below. Completed verification and retained limitations are recorded at the end.

## Goal and agreed behavior

Deliver the .NET 8 REST API with a durable current board state in PostgreSQL. Preserve the independent Game of Life engine, bounded computation, validation, and existing operational safeguards. No authentication is required.

The intended interpretation of the exercise is deliberately hybrid:

| Operation | Behavior | Per-board lock |
|---|---|---|
| Upload | Create a new GUID and persist the initial matrix at generation 0, status Active. | No |
| Next | Load current state, calculate one generation, and persist and return its matrix and generation. Reject an already terminal board. | Yes |
| N ahead | Read one committed current snapshot, calculate N steps in memory, and return a projection without changing storage. | No |
| Final | Simulate from current state until stable or cyclic; atomically persist the detected matrix, generation, terminal status, and metadata. | Yes |
| Fetch | Return persisted current state, generation, status, and available terminal metadata. | No |

For a projection starting at generation G, return generation G + N. A concurrent mutation may finish after the projection reads its snapshot; the projection remains valid for that snapshot and does not retry against a newer one.

## HTTP contract

Next and Final use POST because they mutate stored state. Remove their existing GET mappings; do not retain state-changing GET aliases. Keep the route paths and operation names where appropriate.

| Method and path | Result | Success | Expected errors |
|---|---|---|---|
| `POST /api/v1/boards` | Create board; return id and Location | 201 | 400, 413, 415, 503 |
| `GET /api/v1/boards/{id}` | Current persisted board | 200 | 400, 404, 503 |
| `POST /api/v1/boards/{id}/next` | Advance and persist one generation | 200 | 400, 404, 409, 422, 503 |
| `GET /api/v1/boards/{id}/generations/{n}` | Read-only projection | 200 | 400, 404, 409, 422, 503 |
| `POST /api/v1/boards/{id}/final` | Persist a terminal result, or return the existing terminal result | 200 | 400, 404, 409, 422, 503 |
| `GET /health/live` | Process is serving | 200 | |
| `GET /health/ready` | Database is reachable and migrated | 200 | 503 |

Persisted-state responses include id, generation, dimensions, cells, and status (`Active`, `Stable`, `Cycle`). Terminal responses also include cycle period and the first generation at which that matrix was observed during the final-state run. Projection responses include sourceGeneration and projected generation, dimensions, and cells; they do not claim to have changed terminal status.

Use Problem Details for application errors. Preserve the documented exceptions for content negotiation and requests rejected by Kestrel before the application pipeline. Terminal Next returns 409 with terminal status and a link to fetch the final state. Final-search exhaustion returns 422 with the configured limit and leaves storage unchanged. Lock-wait timeout or server overload returns 503 with Retry-After. Unknown GUIDs return 404 after the applicable lock is acquired. Unexpected failures remain 500 without internal details.

Implemented interpretation choices:

- N = 0 is valid and returns the snapshot unchanged. Projections remain available for terminal boards and do not change stored status.
- Final on an already terminal board returns its saved result without simulation or timestamp changes.
- Upload and ordinary Next leave status Active; Final performs terminal classification. Next does not keep cross-request history to discover oscillators.
- Retrying Next is another advancement if the prior request committed. Serialization prevents lost updates, not duplicate application of client retries. Do not automatically retry ambiguous writes; callers can fetch the current state. Idempotency keys are outside this change.

## Persistence and migration

Keep PostgreSQL 16 and parameterized Npgsql commands. Add `0002_current_board_state.sql`; preserve migration 0001 so existing databases can upgrade.

The board row contains:

- `id uuid primary key`, existing dimensions, and `cells jsonb` containing rectangular nested arrays of integer 0/1 values.
- `generation bigint not null`, initially 0. Use C# long for accumulated generations and checked arithmetic.
- `status text not null`, constrained to Active, Stable, or Cycle.
- `created_at`, `updated_at`, and nullable `completed_at` timestamps.
- Nullable `cycle_start_generation bigint` and `cycle_length integer`, with constraints consistent with status and current generation. Stable has period 1; Cycle has period greater than 1.

Convert existing flattened boolean arrays to nested JSON arrays in row/column order, retaining GUIDs and creation timestamps. Existing rows become generation 0, Active, with updated_at initialized from created_at. Test rectangular, single-row, and single-column conversions. Validate JSON shape and values at the write boundary and reject invalid database representations when loading. Retain meaningful database constraints for dimensions, JSON type, generation, and status/metadata consistency.

Serialize and deserialize matrices as `int[][]`. Map them to the existing immutable Board at the persistence/application boundary. Its compact internal bool[] representation can remain: Board.Next already calculates from a separate immutable input buffer. Do not persist historical matrices or hashes.

Save matrix, generation, status, timestamps, and terminal metadata in one atomic UPDATE. A committed update remains durable if the client loses the response; a failed simulation performs no update.

## Per-board distributed concurrency

Use a PostgreSQL session-level advisory lock as an exclusive semaphore for each board GUID. Every app instance follows the same protocol for both Next and Final. Upload, Fetch, and N-ahead do not take it. Keep the existing process-wide simulation limiter as a separate resource safeguard.

Mutation lifecycle:

1. Open a dedicated Npgsql connection for the operation.
2. Derive a deterministic advisory key from SHA-256 of a fixed namespace plus canonical GUID text, read as a big-endian 64-bit integer with its sign bit set. Do not use runtime GetHashCode. Negative board keys stay separate from the positive migration key; the remaining 63 bits identify boards. A hash collision only serializes unrelated boards.
3. Acquire the session lock with cancellable, bounded waiting, for example pg_try_advisory_lock with asynchronous delays. Give acquisition a validated configurable timeout.
4. After acquisition, load the latest row using that same connection. Check existence and terminal status after waiting.
5. Calculate in memory while retaining the session. Do not hold a normal transaction or SELECT FOR UPDATE lock open during simulation.
6. Persist the complete result atomically through the same connection. Release the advisory lock before returning that connection to its pool.
7. Clean up on every exit: cancellation, 404, terminal rejection, iteration exhaustion, and exceptions. Cleanup uses its own bounded cancellation scope. If unlock cannot be confirmed, ensure the physical session cannot be reused with a held lock.

Do not reconnect and save a result calculated under a lost session lock. Failure of the lock-owning connection aborts the operation. PostgreSQL releases session locks when the session ends. Direct SQL writers bypassing this cooperative protocol are outside the API serialization guarantee.

One GUID serializes across app instances; different GUIDs progress concurrently within resource limits. Waiting mutations also consume connections, so bound admission and lock waiting and leave pool capacity for other operations. Do not introduce Redis or a database-wide board lock.

References: [PostgreSQL advisory locks](https://www.postgresql.org/docs/16/explicit-locking.html#ADVISORY-LOCKS), [Npgsql connection and pooling lifecycle](https://www.npgsql.org/doc/basic-usage.html).

## Engine and terminal-state accounting

Keep GameOfLife.Core independent of HTTP, Npgsql, and configuration binding. Preserve fixed dimensions and dead cells outside the grid.

Extend FinalState and Simulation.FindFinalState to distinguish cycle start, detection step, and period. The dictionary of immutable boards and first-seen steps already gives collision-safe repeated-state detection; retain it initially. Reset history for each Final call.

If a run starts at persisted generation G, and a matrix first appears at local step S and repeats at local step D, persist generation G + D, that repeated matrix, cycle_start_generation G + S, and period D - S. Period 1 is Stable; a longer period is Cycle. Cycle start means first observed during this run, not an inferred historical global origin.

Examples: a newly uploaded blinker becomes Cycle at generation 2 with cycle start 0 and period 2. An already stable upload becomes Stable when its first step repeats, at generation 1 with cycle start 0 and period 1. The earlier API reported cycle start as its main generation; update tests and examples accordingly.

Maintain cancellation between generations. Exceeding the final iteration limit leaves the board at its prior generation and Active status. Return an error instead of saving partial progress.

## Code organization and limits

- Keep the two projects. Add a small board-operation service inside the API project to coordinate reads, simulation, terminal decisions, and writes; retain HTTP mapping in BoardEndpoints.
- Extend IBoardRepository/NpgsqlBoardRepository to return a persisted-state record containing Board and metadata. Give mutations a disposable locked-session boundary that owns connection, lock, read, update, and cleanup together. Avoid a lock helper that returns its pooled connection before the operation finishes.
- Update BoardContracts, BoardMapper, BoardEndpoints, dependency registration, configuration validation, and OpenAPI together.
- Store dimension, iteration, concurrency, lock-wait, and resource-budget defaults in appsettings.json. Validate positive values and combined cell/iteration/concurrency budgets without duplicating policy ceilings in attributes. Retain overflow protection and limits imposed by arrays, timers, and worker capacity.
- Budget retained cell buffers during Final explicitly; document that JSON conversion and other process overhead require additional memory. Preserve request-body limits, pool headroom, and admission control.

## Completed implementation sequence and acceptance checks

1. **Domain and contracts.** Add status/current-state metadata, revise generation accounting, and define results/errors. Retain engine correctness tests; add stable/cycle detection-generation and starting-offset cases.
2. **Schema and persistence.** Add the upgrade migration and JSON mapping; implement atomic updates. Test upgrading existing rows, round trips, metadata constraints, and all-or-nothing state changes with real PostgreSQL.
3. **Lock ownership.** Implement session lifecycle and bounded acquisition. Test independent connections/data sources: the same GUID blocks, a different GUID proceeds, and completion/failure paths release ownership. Test session termination and pooled-connection reuse explicitly.
4. **API behavior.** Wire the service and POST routes. Replace tests requiring identical repeated Next responses with sequential advancement. Test Next/Next and Next/Final contention using two API hosts sharing a database, final replay, terminal rejection, and projection after advancement. Use synchronization barriers instead of timing-only assertions.
5. **Failure and durability.** Prove iteration exhaustion and pre-write cancellation save nothing; row fields change atomically; session loss cannot cause a stale write. Verify committed current and terminal states survive API/database restart. Exercise lock timeout without waiting indefinitely.
6. **Documentation and scripts.** Update README, OpenAPI descriptions/tests, comments claiming immutable storage, smoke-test.sh, verify-restart.sh, and measure-worst-case.sh. Show POST calls, current-generation semantics, projection behavior, terminal errors, migration steps, and locking rationale. Benchmarks must distinguish same-GUID serialization from different-GUID concurrency and avoid mistaking saved terminal responses for fresh simulation. Replace obsolete measured claims with new results.
7. **Verification.** Run the .NET 8 build, core and PostgreSQL integration suites, dotnet format --verify-no-changes, then smoke/restart checks against an isolated Compose project and volume. Keep restart/crash tests isolated from existing development stacks. Record exact checks and limitations in the completion report.

Use the .NET 8 SDK selected by global.json and Docker for PostgreSQL integration tests. Retain net8.0 and global.json; select the installed matching SDK rather than changing the target to accommodate a different default SDK. Report verification results only after running the checks.

## Updated baseline

The implementation is based on d27b454. It retains the latest thread-pool minimum adjustment and its three startup tests. It also preserves d16c830's qualified error-format descriptions, non-destructive environment-file example, database-stall limitations, tooling/support notes, and ignored compose.override.yaml.

## Delivery

Deliver the complete change as one cohesive pull request, covering code, migration, tests, documentation, and scripts. Use branch `feature/current-board-state` and title "Persist current board state and serialize board mutations". Complete the acceptance checks before opening the PR; describe the resulting behavior, migration, validation, and material limitations.

## Documentation completion criteria and scope

The final README must describe implemented behavior with reproducible commands and examples. Remove the immutable-upload rationale and boolean[] persistence description. Explain JSONB, Active/Stable/Cycle, detection generation versus cycle start, advisory-lock lifetime/timeout, retry semantics, limits, and measured resource costs. Then update this plan with completed work and remaining limitations, without claiming unchecked production guarantees.

Keep scope proportionate to the take-home: no authentication, Redis, background jobs, generic repository framework, persistent state history, or unrelated performance refactor.

## Completion record

- Added `BoardStatus`, detection-step accounting in `FinalState`, and `StoredBoard`. The immutable engine remains independent of HTTP and persistence.
- Added JSONB migration 0002 and `BoardJson`; existing IDs, matrices, and creation timestamps survive the upgrade. The migration is a coordinated stop-and-upgrade operation, not a rolling deployment with old binaries.
- Added `IBoardMutationSession`, session advisory locks, atomic current-state updates, bounded acquisition, and safe pool cleanup. Next and Final use the same connection from acquisition through save and release.
- Added `BoardService`, POST mutations, read-only projections with sourceGeneration, terminal errors/replay, configurable limits, and matching OpenAPI/docs/scripts.
- Centralized defaults in appsettings.json: 256-by-256 dimensions, 500 steps, eight concurrent simulations, five-second lock acquisition, 65,536 cells, 67,108,864 cell-steps, and 512 MiB of retained cell buffers. Each is configurable; the former fixed dimension/iteration/concurrency/timeout ceilings were removed. Startup checks positive values, combined budgets, and runtime constraints with overflow-safe arithmetic. Connection settings reject multiplexing and require pool headroom. Retained-buffer accounting includes the initial board and computed boards; conversion/object/runtime overhead is additional.
- Verified a .NET 8 build with zero warnings/errors, all 263 tests (92 core, 171 API/configuration/PostgreSQL), formatting, 19 smoke checks, and 13 restart/crash checks. Configuration tests cover overriding the former ceilings, inclusive budgets, positive values, overflow, and runtime constraints. Integration tests include two API hosts, competing pools, cancellation, session loss, failed writes, and reuse of the same physical pooled connection without a held advisory lock.
- Measured default-limit work in the Release Docker image: mean 0.815 s for sequential projections, 0.802 s for sequential Final searches, and 0.928 s with eight distinct GUIDs. Same-GUID contention serialized seven full searches and returned one lock-timeout 503; failed searches left storage unchanged. The README records the machine, workload, ranges, and interpretation.

Retained limitations: Final stores seen matrices in memory only for its current run; client retries of Next are not idempotent; direct SQL writers must cooperate with the advisory-lock protocol; session-affine database connections are required; long database stalls can consume admission permits until database timeouts. Authentication, retention, TLS termination, background jobs, and idempotency keys remain outside this change.
