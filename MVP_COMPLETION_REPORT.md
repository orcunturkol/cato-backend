# CATO MVP Document vs Codebase - Completion Report

**Date:** 2026-04-09 (updated)
**Source Document:** CATO'dan SelfPub'a Data Yapisi MVP.pdf
**Project:** cato-backend

---

## A. Database Tablolari (Data Structure)

| Tablo (Document) | DB Schema | Entity | API/Handler | Status |
|---|---|---|---|---|
| **MAIN GAME** | Done | Done | Done (CRUD + Enrich) | Done |
| **OWNED GAMES** | Done | Done (OwnedGameData) | Done (Ingest + Query) | Done |
| **GAME GENRE** | Done | Done | Done (via enrichment) | Done |
| **GENRE TAG** | Done | Done | Done (via enrichment) | Done |
| ~~**TARGET MATCH**~~ | ~~Done~~ | ~~Missing~~ | ~~Missing~~ | ~~**Missing**~~ → **Done** |
| **STEAM SALE FINANCIAL** | Done | Done | Done (Ingest + Query) | Done |
| **STEAM TRAFFIC** | Done | Done | Done (Ingest + Query) | Done |
| ~~**ACTION**~~ | ~~Done~~ | ~~Missing~~ | ~~Missing~~ | ~~**Missing**~~ → **Done** |
| ~~**ACTION TARGET**~~ | ~~Done~~ | ~~Missing~~ | ~~Missing~~ | ~~**Missing**~~ → **Done** |
| ~~**ACTION IMPACT**~~ | ~~Done~~ | ~~Missing~~ | ~~Missing~~ | ~~**Missing**~~ → **Done** |
| **LEGAL ENTITY** | Done | Done | Done (via game) | Done |

---

## B. Extension Tablolari

| Tablo (Document) | DB Schema | Entity | API/Handler | Status |
|---|---|---|---|---|
| **GAME CCU LOGS** | Done (ccu_history) | Done (CcuHistory) | Done (Ingest + Query + Live) | Done |
| **GAME DEMO** | Missing | Missing | Missing | **Missing** |
| **USER ID** (Influencer/Playtime) | Missing | Missing | Missing | **Missing** |

---

## C. Modul 1: Veri Toplama ve Backend (Ingestion Engine)

| Story | Status | Notes |
|---|---|---|
| **1.1 Otomatik CCU Takibi** (15dk) | Partial | Automated via external Python orchestrator `catoptric-data-collector/orchestrators/run_ccu.py` on cron `0 */4 * * *` → publishes to RabbitMQ → backend consumer ingests to `ccu_history`. **Granularity is every 4h, not 15 min** as the MVP doc asks. |
| **1.2 Gunluk Finansal Senkronizasyon** | Complete | `orchestrators/run_daily_financial.py` orchestrator added; cron entry `0 4 * * *` documented in `orchestrator_commands.txt`. Calls `IPartnerFinancialsService.GetChangedDatesForPartner` + `GetDetailedSales` (paginated), saves per-app JSON, publishes `steam_financial` messages to RabbitMQ → backend ingests to `steam_sale_financial`. |
| **1.3 Otomatik Oyun Tanimlama (Enrichment)** | Done | `POST /games/{id}/enrich` + `POST /games/re-enrich` + Genre/Tag auto-fill |

---

## D. Modul 2: Admin Paneli ve Veri Girisi (Interface)

| Story | Status | Notes |
|---|---|---|
| ~~**2.1 Aksiyon Yonetimi ve Iliskilendirme**~~ | ~~**Missing**~~ | → **Done** — Full marketing module implemented: Action/GameAction/ActionTarget/ActionImpact/TargetMatch/MarketingTarget entities, services, handlers, validators, controllers (20 endpoints). |
| **2.2 Sourcing / Rakip Ekleme** | Done | `POST /games` with GameType="Competitor"/"Sourcing" + SteamPicsWatcherService auto-discovers games |

---

## E. Modul 3: Raporlama ve Analiz (Intelligence)

| Story | Status | Notes |
|---|---|---|
| **3.1 Action Impact Gorsellestirmesi** | Partial | ActionImpact entity + CRUD now exist. Still missing: **dedicated report endpoint** that overlays action markers onto CCU + Traffic timeseries for visualization. |
| **3.2 Bolgesel Gelir Haritasi** | Partial | Financial data with country_code exists, but **no dedicated report endpoint** for top-10 country revenue + refund rates. |

---

## F. Teknik Kisitlar (Non-Functional)

| Requirement | Status |
|---|---|
| **Rate Limiting** | Done (1.5s delay + exponential backoff in SteamApiService) |
| **Idempotency** | Done (UNIQUE constraints + upsert logic) |
| **Security** (no hardcoded keys) | Done (appsettings.json / env vars) |

---

## Summary

| Category | Items | Done | Partial | Missing |
|---|---|---|---|---|
| **Database Tables (MVP)** | 12 | 12 | 0 | 0 |
| **Extension Tables** | 3 | 1 | 0 | 2 |
| **User Stories** | 8 | 4 | 3 | 1 |
| **Non-functional** | 3 | 3 | 0 | 0 |

### Overall Completion: ~80%

---

## ✅ Completed Since Last Report (2026-04-06)

### Marketing Module (was biggest gap)
- `MarketingTarget` entity + CRUD (`/api/marketing-targets`, 5 endpoints)
- `MarketingAction` entity + CRUD + game/target association + impact upsert (`/api/actions`, 12 endpoints)
- `TargetMatch` entity + list/upsert (`/api/target-matches`, 2 endpoints, 1 upsert)
- `ActionImpact` entity + upsert/get (nested under `/api/actions/{id}/impact`)
- `User` + `UserProfile` entities + DbContext config (were missing from DbContext despite entities existing)
- Migration `20260406120000_AddMarketingModule` + Designer.cs + updated ModelSnapshot

---

## ✅ Completed Since Last Report (2026-04-09)

### Priority 4 — New Tables (+ deferred Priority 1 item)

| Endpoint | Route | Entity / Table | Notes |
|---|---|---|---|
| **News ingestion** | `POST /api/ingestion/news` | `GameNews` / `game_news` | Parses `appnews.newsitems[]` from Steam API JSON. Upsert on `(GameId, ExternalId)`. Source = `"news"`. |
| **Patch Notes ingestion** | `POST /api/ingestion/patch-notes` | `GameNews` / `game_news` | Parses `entries[]` from RSS-feed JSON. ExternalId = `id` URL field. Source = `"patch_notes"`. RFC 2822 date parsing. |
| **Active Users History** | `POST /api/ingestion/active-users` | `ActiveUsersHistory` / `active_users_history` | Parses `active_users_history[]` array (unix ms timestamps, dau, mau). Upsert on `(GameId, RecordedAt)`. |
| **Demo Downloads** | `POST /api/ingestion/demo-downloads` | `DemoDownload` / `demo_download` | Parses two-section Steamworks CSV (Region block then Country block, split by `Country,0,` separator row). Optional `demoAppId` form param. Upsert on `(GameId, SnapshotDate, GeoType, GeoName)`. |

Migration `AddPriority4Tables` covers all schema changes.

---

## ✅ Completed Since Last Report (2026-04-07)

### Priority 1 — New Ingestion Endpoints

| Endpoint | Route | Entity / Table | Notes |
|---|---|---|---|
| **Regional Price History** | `POST /api/ingestion/regional-prices` | `PriceSnapshot` / `price_snapshot` | Parses per-currency JSON (`regional_price_history.json`). Unique index extended to `(GameId, CapturedAt, Currency)`. Inserts one row per currency per snapshot. |
| **Wishlist Insights** | `POST /api/ingestion/wishlist-insights` | `WishlistInsight` / `wishlist_insight` (**new table**) | Parses `alsoWishlisted` array from `wishlist_insights.json`. Stores related AppId, link score, price, genres (JSONB), revenue. Upsert on `(GameId, SnapshotDate, RelatedAppId)`. |
| **Store Traffic Breakdown** | `POST /api/ingestion/store-traffic` | `SteamTrafficBreakdown` / `steam_traffic_breakdown` (**new table**) | Parses Steamworks per-feature CSV (`Page / Category`, `Page / Feature`, `Impressions`, `Visits`). Handles quoted numeric cells. Upsert on `(GameId, SnapshotDate, PageCategory, PageFeature)`. |

All three endpoints follow the standard `IngestCommand → Handler → Validator → IIngestionService` MediatR pattern and write to `ingestion_log`.

Migration `20260407163709_AddNewIngestionTables` covers all schema changes.

**Still deferred from Priority 1:**
- **Active Users History** (`active_users_history.json`, dau/mau fields) — schema decision pending (new table vs. nullable columns on `ccu_history`).

---

## 🔜 NEXT: What Remains

### Priority 1 — Missing Ingestion Endpoints ~~(tables/entities already exist, just need handlers)~~

| Task | Status |
|---|---|
| ~~**Store Traffic CSV ingestion**~~ | ~~Missing~~ → **Done** (`POST /api/ingestion/store-traffic`) |
| ~~**Wishlist Insights ingestion**~~ | ~~Missing~~ → **Done** (`POST /api/ingestion/wishlist-insights`) |
| ~~**Regional Price ingestion**~~ | ~~Missing~~ → **Done** (`POST /api/ingestion/regional-prices`) |
| ~~**Active Users History ingestion**~~ | ~~**Still missing**~~ → **Done** (`POST /api/ingestion/active-users`, `active_users_history` table) |

---

### Priority 2 — Scheduled Jobs (automation for existing ingestion)

**Architecture note:** scheduling is handled **externally** via the Python `catoptric-data-collector` repo (cron → collector script → RabbitMQ → backend consumer), not via .NET `IHostedService`. Orchestrator scripts live in `catoptric-data-collector/orchestrators/` and cron entries are documented in `orchestrators/orchestrator_commands.txt`.

#### ✅ Already automated
| Job | Orchestrator | Cron | Target |
|---|---|---|---|
| CCU collection | `run_ccu.py` | `0 */4 * * *` | `ccu_history` via `steam_current_players` messages |
| Daily group member count | `run_daily_db.py` | `0 3 * * *` | `group_member_count_snapshot` |
| SteamDB snapshots | `run_daily_steamdb.py` | `30 3 * * *` | `steamdb_snapshot` |

#### 🔜 Still needed
| Task | What's needed |
|---|---|
| ~~**Daily financial sync at 04:00 UTC** (Story 1.2)~~ | ✅ Done — `orchestrators/run_daily_financial.py` created, cron `0 4 * * *` documented. |
| **Tighten CCU cadence to 15 min** (Story 1.1, optional) | Current cron is every 4h. If MVP literally requires 15 min granularity, change cron to `*/15 * * * *` and confirm Steam API rate limits + DB volume are acceptable. |

---

### Priority 3 — Reporting Endpoints (MVP Story 3.1 + 3.2)

| Task | What's needed |
|---|---|
| **Regional Revenue Report** (`GET /api/reports/revenue-by-region`) | Query `steam_sale_financial` grouped by `country_code`, return top N with gross revenue + refund rate. No new table needed. |
| **Action Impact Timeline** (`GET /api/reports/action-timeline/{gameId}`) | Join `ccu_history` + `steam_traffic` + `action` + `game_action` for a date range. Return timeseries + action markers. No new table needed. |

---

### Priority 4 — New Tables from Excel (not in MVP doc, but in Excel sheet)

| Task | Schema needed | Data source | Status |
|---|---|---|---|
| ~~**GAME DEMO table**~~ | ~~`game_demo(...)`~~ | ~~partner CSV for downloads~~ | → **Done** — `demo_download` table, `POST /api/ingestion/demo-downloads` (CSV with Region/Country sections, optional `demoAppId` form param) |
| ~~**News / Patch Notes table**~~ | ~~`game_news(...)`~~ | ~~`news.json` + `patch_notes.json`~~ | → **Done** — `game_news` table with `source` discriminator, `POST /api/ingestion/news` + `POST /api/ingestion/patch-notes` |
| ~~**Active Users History (DAU/MAU)**~~ | ~~deferred from Priority 1~~ | ~~`active_users_history.json`~~ | → **Done** — `active_users_history` table, `POST /api/ingestion/active-users` |
| **Active Users by Region table** | `player_region(id, game_id FK, snapshot_date, region_code, player_count)` | `Data Pulled/active_users_regions.json` — file only has metadata (timestamp, game_id, game_name), no actual regional breakdown | **Deferred** — no regional data available |

---

### Priority 5 — Lower Priority Enrichments

| Task | Notes |
|---|---|
| **User/Influencer table** | `IPlayerService.GetOwnedGames` + `ISteamUser.GetPlayerSummaries`. Useful for influencer analysis. |
| **Achievements table** | `ISteamUserStats/GetGlobalAchievementPercentagesForApp`. File in `Data Pulled/achievements.json`. |
| **Reviews ingestion** | `store.steampowered.com/appreviews/{appId}`. File in `Data Pulled/recent_reviews.json`. Enrich `main_game` review data or new table. |
| **Bundle Sales ingestion** | Separate from regular `steam_sale_financial`. Needs `package_id` differentiation or new table. |

---

## Key Files for Next Session

| Purpose | Path |
|---|---|
| Ingestion handler pattern | `src/Cato.API/Services/Handlers/Ingestion/IngestPeakCcuHandler.cs` |
| Background service pattern | `src/Cato.Infrastructure/Background/SteamPicsWatcherService.cs` |
| Ingestion controller | `src/Cato.API/Controllers/IngestionController.cs` |
| Ingestion service | `src/Cato.API/Services/IngestionService.cs` + `IIngestionService.cs` |
| DbContext (add new tables here) | `src/Cato.Infrastructure/Database/CatoDbContext.cs` |
| New entities (2026-04-07) | `src/Cato.Domain/Entities/WishlistInsight.cs`, `SteamTrafficBreakdown.cs` |
| Data files | `Data Pulled/` directory |
| EF migration command | `dotnet ef migrations add <Name> --project src/Cato.Infrastructure --startup-project src/Cato.API` |
