# Bulk-import: update GameType on existing games

**Date:** 2026-06-17
**Status:** Approved

## Problem

The `POST /api/games/bulk-import` endpoint already accepts a `gameType` form
parameter and creates every appid in the uploaded CSV/XLSX with that type. But
when an appid already exists in the database (e.g. it was imported earlier as
`Sourcing`, or discovered and enriched by the PICS watcher), the bulk path calls
`GameService.CreateGameAsync`, which fails with `"already exists"`. The handler
counts that as a skip — the existing game is never updated.

Desired behavior: a re-import with the same appids should **update** the existing
games' `GameType` to the newly-chosen value (e.g. flip `Sourcing` → `Owned`)
rather than skipping them, and re-enrich them from Steam.

## Decisions

- **Create-or-update for existing appids.** Existing games get their `GameType`
  updated to the imported value; new appids are created as today.
- **Always re-enrich.** Every appid in the file is re-enriched from Steam on
  every import, whether it was just created or already existed — keeps Steam data
  fresh. (Chosen over "only re-enrich when the type changed.")
- **Upsert logic lives in `GameService`** as a new `UpsertGameAsync` method
  (Approach B), leaving `CreateGameAsync` and the single-create endpoint
  untouched.

## Design

### 1. `UpsertGameResult` record

A small result carrier so the caller knows the game id (for enrichment) and
whether it was created or updated (for logging):

```csharp
public record UpsertGameResult(GameDto Game, bool WasCreated);
```

### 2. `IGameService.UpsertGameAsync`

```csharp
Task<Result<UpsertGameResult>> UpsertGameAsync(CreateGameCommand command, CancellationToken ct = default);
```

Logic in `GameService`:

1. Look up the game by `AppId` (tracked).
2. **Not found** → call the existing `CreateGameAsync(command)`. On success
   return `{ Game, WasCreated = true }`; on failure propagate the failure.
3. **Found** → if `command.GameType` is non-null and differs from the game's
   current `GameType`:
   - capture `oldType`, set `game.GameType = command.GameType`,
   - `await _db.SaveChangesAsync(ct)`,
   - `await _redisSync.UpdateAsync(game.AppId, oldType, game.GameType, game.Name, ct)`
     — required so the Redis tracked sets (Sourcing/Owned) move correctly, the
     same call `UpdateGameAsync` already makes.

   If the type is unchanged (or null), make no DB write and no Redis call.
   Reload the game with navigation includes (`Developer`, `Publisher`, `Genres`,
   `Tags`) and return `{ Game, WasCreated = false }`.

`UpsertGameAsync` does **not** enrich — enrichment stays solely in the handler.

### 3. `BulkImportGamesHandler` background loop

Replace the `CreateGameAsync` call with `UpsertGameAsync`. Then **always** call
`EnrichGameFromSteamAsync(result.Data.Game.Id)` regardless of created/updated.

Counters become `created`, `updated`, `enriched`, `failed`:

- upsert failure → log warning, `failed++`, continue.
- `WasCreated` → `created++`, else `updated++`.
- enrich success → `enriched++`, else log warning.

The completion log line gains the `updated` count.

### 4. Unchanged

- Endpoint signature and the `gameType` form param (already in place).
- `BulkImportResult(int Queued)` — the response returns `202 Accepted` before
  background processing, so it cannot report created/updated outcomes; those stay
  in the logs.
- `CreateGameAsync`, the single-create endpoint, and all other callers.

## Files touched

- `src/Cato.API/Services/IGameService.cs` — add `UpsertGameAsync`.
- `src/Cato.API/Services/GameService.cs` — implement `UpsertGameAsync` + add
  `UpsertGameResult` record (or place the record in `Models/Games/`).
- `src/Cato.API/Services/Handlers/Games/BulkImportGamesHandler.cs` — use
  `UpsertGameAsync`, always enrich, add `updated` counter.

## Testing

No test projects exist in the repo. Verification is `dotnet build` plus manual
endpoint testing (owned by the user): import a CSV as `Sourcing`, confirm
creation; re-import the same appids as `Owned`, confirm `GameType` flips and the
games re-enrich.
