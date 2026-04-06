# CATO MVP Document vs Codebase - Completion Report

**Date:** 2026-04-06 (updated)
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
| ~~**GAME ACTION**~~ | ~~Done~~ | ~~Missing~~ | ~~Missing~~ | ~~**Missing**~~ → **Done** |
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
| **1.1 Otomatik CCU Takibi** (15dk) | Partial | SteamKit live CCU exists, but **no automated 15-min cron job**. Manual `POST /steamkit/ccu/{appId}/save` only. |
| **1.2 Gunluk Finansal Senkronizasyon** | Partial | Ingestion endpoint exists, but **no scheduled 04:00 UTC auto-sync**. File-based only, no direct IPartnerFinancialsService API call. |
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

### Overall Completion: ~72%

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

## 🔜 NEXT: What Remains

### Priority 1 — Missing Ingestion Endpoints (tables/entities already exist, just need handlers)

These are the quickest wins — the database table, EF entity, and DTO pattern are all already in place.

| Task | What's needed | Data file available? |
|---|---|---|
| **Store Traffic CSV ingestion** | `POST /api/ingestion/traffic` — reads `steam_traffic` CSV from partner portal | No sample file in `Data Pulled/` |
| **Active Users History ingestion** | `POST /api/ingestion/active-users` — maps to `ccu_history` table | Yes — `Data Pulled/active_users_history.json` |
| **Wishlist Insights ingestion** | `POST /api/ingestion/wishlist-insights` — maps to `owned_game_data` | Yes — `Data Pulled/wishlist_insights.json` |
| **Regional Price CSV ingestion** | `POST /api/ingestion/regional-prices` — enrich `price_snapshot` with per-region rows | Yes — `Data Pulled/regional_price_history.json` |

**Pattern to follow:** `src/Cato.API/Services/Handlers/Ingestion/IngestPeakCcuCommandHandler.cs` (reads JSON file, upserts to DB via EF).

---

### Priority 2 — Scheduled Jobs (automation for existing ingestion)

Both are documented in MVP Story 1.1 and 1.2.

| Task | What's needed |
|---|---|
| **15-min CCU cron** | `IHostedService` that calls `SteamKitDataService.SaveCcuAsync()` for all active games every 15 min. Use `PeriodicTimer`. Register in `Program.cs`. |
| **Daily financial sync at 04:00 UTC** | `IHostedService` with `PeriodicTimer` (daily). Currently ingestion is file-based — either keep file-watching or integrate `IPartnerFinancialsService` API if credentials available. |

**Pattern to follow:** `src/Cato.Infrastructure/Background/SteamPicsWatcherService.cs` (IHostedService with loop + CancellationToken).

---

### Priority 3 — Reporting Endpoints (MVP Story 3.1 + 3.2)

| Task | What's needed |
|---|---|
| **Regional Revenue Report** (`GET /api/reports/revenue-by-region`) | Query `steam_sale_financial` grouped by `country_code`, return top N with gross revenue + refund rate. No new table needed. |
| **Action Impact Timeline** (`GET /api/reports/action-timeline/{gameId}`) | Join `ccu_history` + `steam_traffic` + `action` + `game_action` for a date range. Return timeseries + action markers. No new table needed. |

---

### Priority 4 — New Tables from Excel (not in MVP doc, but in Excel sheet)

| Task | Schema needed | Data source |
|---|---|---|
| **GAME DEMO table** | `game_demo(id, parent_game_id FK, demo_app_id, demo_type, release_date, deactivate_date, review_count, review_score, ccu_peak, downloads_by_region JSONB)` | `store.steampowered.com/api/appdetails/?appids={demoAppId}` (public) + partner CSV for downloads |
| **News / Patch Notes table** | `game_news(id, game_id FK, gid, title, url, author, contents, date, feed_label)` | `ISteamNews/GetNewsForApp` (public API) — file in `Data Pulled/news.json` + `patch_notes.json` |
| **Active Users by Region table** | `player_region(id, game_id FK, snapshot_date, region_code, player_count)` | `Data Pulled/active_users_regions.json` already available |

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
| Ingestion handler pattern | `src/Cato.API/Services/Handlers/Ingestion/IngestPeakCcuCommandHandler.cs` |
| Background service pattern | `src/Cato.Infrastructure/Background/SteamPicsWatcherService.cs` |
| Ingestion controller | `src/Cato.API/Controllers/IngestionController.cs` |
| DbContext (add new tables here) | `src/Cato.Infrastructure/Database/CatoDbContext.cs` |
| Data files | `Data Pulled/` directory |
| EF migration command | `dotnet ef migrations add <Name> --project src/Cato.Infrastructure --startup-project src/Cato.API` |
