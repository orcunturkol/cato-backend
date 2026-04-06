# CATO - Data Source to Table Mapping

**Date:** 2026-04-04
**Source:** Catoptric Games System - Data Table.xlsx + MVP Document

This document maps each data source from the Excel spreadsheet to the database table it feeds, how data is obtained, and the current implementation status.

---

## Legend

- **Method:** CSV Upload / API (public) / API (private/partner) / Scrape
- **Status:** Done = endpoint + handler exist | Partial = table exists but no ingest | Missing = no table/entity

---

## 1. DATA SOURCES THAT FEED EXISTING TABLES

### MAIN GAME Table

| Data Source | Method | Endpoint/Source URL | Status |
|---|---|---|---|
| **Steam Store: App Details** | API (public) | `store.steampowered.com/api/appdetails/?appids={id}` | Done - `POST /api/games/{id}/enrich` auto-fills name, price, release date, developer, descriptions, images, platforms, genres, tags |

### CCU HISTORY Table (GAME CCU LOGS)

| Data Source | Method | Endpoint/Source URL | Status |
|---|---|---|---|
| **Steam Current Players (Public API)** | API (public) | `api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/` | Done - `POST /api/steamkit/ccu/{appId}/save` (SteamKit2) |
| **Player Counts (Peak CCU)** | CSV Upload (private) | `partner.steampowered.com/app/players/{appId}/` | Done - `POST /api/ingestion/peak-ccu` reads from `peak_ccu_history.json` |
| Sample file: `Data Pulled/peak_ccu_history.json` | | | |

### STEAM SALE FINANCIAL Table

| Data Source | Method | Endpoint/Source URL | Status |
|---|---|---|---|
| **Sales** | CSV Upload (private) | `partner.steampowered.com/app/details/{appId}/` | Done - `POST /api/ingestion/financial` reads from `regional_sales_history.json` |
| **Sales By Country** | CSV Upload (private) | `partner.steampowered.com/app/details/{appId}/` | Done - same endpoint, includes `country_code` |
| **Sales By Region** | CSV Upload (private) | `partner.steampowered.com/app/details/{appId}/` | Done - same endpoint, regional breakdown |
| **Bundle Package Sales** | CSV Upload (private) | `partner.steampowered.com/bundles/details/{bundleId}/` | **Missing** - no bundle sales ingestion |
| Sample file: `Data Pulled/regional_sales_history.json` | | | |

### STEAM TRAFFIC Table

| Data Source | Method | Endpoint/Source URL | Status |
|---|---|---|---|
| **Store Traffic** | CSV Upload (private) | `partner.steamgames.com/apps/navtrafficstats/{appId}` | **Missing** - table exists, entity exists, but **no CSV ingestion endpoint for traffic data** |
| **Nav Stats** | CSV Upload (private) | `partner.steamgames.com/pub/navstats/{publisherId}` | **Missing** - publisher-level traffic, no ingestion |

### OWNED GAMES Table (OwnedGameData)

| Data Source | Method | Endpoint/Source URL | Status |
|---|---|---|---|
| **Wishlist Action Summary** | CSV Upload (private) | `partner.steampowered.com/app/wishlist/{appId}/` | Done - `POST /api/ingestion/wishlists` reads from `steamworks_wishlists.csv` |
| **Wishlists By Country** | CSV Upload (private) | `partner.steampowered.com/app/wishlist/{appId}/` | Partial - table has no country breakdown |
| **Wishlists By Region** | CSV Upload (private) | `partner.steampowered.com/app/wishlist/{appId}/` | Partial - table has no region breakdown |
| Sample files: `Data Pulled/steamworks_wishlists.csv`, `wishlist_activity.json` | | | |

### GAME GENRE + GENRE TAG Tables

| Data Source | Method | Endpoint/Source URL | Status |
|---|---|---|---|
| **Steam Store: App Details** (genres/categories) | API (public) | `store.steampowered.com/api/appdetails` | Done - auto-populated during game enrichment |
| **Steam Store Page Tags** | Scrape | Store page top tags | Done - `SteamApiService.GetUserTagsAsync()` scrapes user tags |

### GROUP MEMBER COUNT Table

| Data Source | Method | Endpoint/Source URL | Status |
|---|---|---|---|
| **Steam Group Member Count** | Scrape | `steamcommunity.com/search/groups/?text={name}` | Done - background scraping + `POST /api/ingestion/group-members` |
| Sample file: `Data Pulled/group_member_count.json` | | | |

### STEAMDB SNAPSHOT Table

| Data Source | Method | Endpoint/Source URL | Status |
|---|---|---|---|
| **SteamDB Most Wished Ranking** | Scrape | `steamdb.info/stats/mostwished/` | Done - `POST /api/ingestion/steamdb-snapshot` |
| **SteamDB Wishlist Activity Ranking** | Scrape | `steamdb.info/stats/wishlistactivity/` | Done - same endpoint, different source type |

### PRICE SNAPSHOT Table

| Data Source | Method | Endpoint/Source URL | Status |
|---|---|---|---|
| **Prices** (regional pricing) | CSV (private) | `partner.steamgames.com/pricing/dashboard/{publisherId}` | Partial - `SteamPriceWatcherService` tracks USD price, but **no regional price CSV ingestion** |
| Sample file: `Data Pulled/regional_price_history.json` | | | |

---

## 2. DATA SOURCES WITH NO TARGET TABLE YET (Missing Tables)

### Needs: GAME DEMO Table

| Data Source | Method | Source URL | What it provides |
|---|---|---|---|
| **Demo Downloads by Region** | CSV (private) | `partner.steampowered.com/nav_regions.php?downloads=1&appID={demoAppId}` | Demo download counts by country/region |
| **Demo Median Time** | Scrape (private) | `partner.steampowered.com/app/details/{demoAppId}/` | Median play time for demo |
| **Steam Store: App Details** (for demo) | API (public) | `store.steampowered.com/api/appdetails/?appids={demoAppId}` | Parent AppID link, demo metadata |

### Needs: USER/INFLUENCER Table

| Data Source | Method | Source URL | What it provides |
|---|---|---|---|
| **User Owned Games** | API (public) | `api.steampowered.com/IPlayerService/GetOwnedGames/v0001/` | Steam user's game library + playtime per game |
| **User Details** | API (public) | `api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/` | Steam profile info (name, avatar, country) |
| **User Achievements** | API (public) | `api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v0001/` | Per-user achievement completion |

### Needs: ACTION / GAME ACTION / ACTION TARGET / ACTION IMPACT / TARGET MATCH Tables

| Data Source | Method | What it provides |
|---|---|---|
| **Internal Operations** | Manual Entry (Admin Panel) | Campaign definitions, target assignments, action-game links |
| **Discount History** | Scrape (private) | `partner.steamgames.com/packages/discounts/{packageId}` - past/planned discounts + results |
| **Daily Deals Dashboard** | Scrape (private) | `partner.steamgames.com/promotion/dailydeals/dashboard/{publisherId}` - daily deal performance |
| **Recap Steam Next Fest** | Scrape (private) | `partner.steamgames.com/recap/nextfest/` - Next Fest participation results |

---

## 3. DATA SOURCES NOT YET MAPPED TO ANY TABLE

These exist in the Excel but don't clearly map to an MVP table yet. They could enrich existing tables or require new ones:

| Data Source | Method | Source URL | Potential Table |
|---|---|---|---|
| **Achievements** | API (public) | `ISteamUserStats/GetGlobalAchievementPercentagesForApp` | New: game_achievement or enrich main_game |
| **News / Patch Notes** | API (public) | `ISteamNews/GetNewsForApp` + `steamcommunity.com/games/{id}/rss` | New: game_news / game_patch_note |
| **Reviews** | API (public) | `store.steampowered.com/appreviews/{appId}?json=1` | New: game_review or enrich main_game review_count/score |
| **Wishlist Conversions by Cohort** | CSV (private) | `partner.steampowered.com/app/wishlist/{appId}/` | Enrich: owned_game_data or new cohort table |
| **Wishlist Conversion Rate** | Scrape (private) | `partner.steampowered.com/app/wishlist/{appId}/` | Enrich: owned_game_data |
| **Players by Region** | Scrape (private) | `partner.steampowered.com/app/players/{appId}/` | New: player_region or enrich ccu_history |
| **Main Game Median Time** | Scrape (private) | `partner.steampowered.com/app/details/{appId}/` | Enrich: main_game (add median_playtime field) |
| **Active Users History** | JSON (private) | Already in `Data Pulled/active_users_history.json` | Enrich: ccu_history (DAU vs CCU) |
| **Active Users Regions** | JSON (private) | Already in `Data Pulled/active_users_regions.json` | New: player_region table |

---

## 4. EXISTING DATA FILES (Data Pulled/) vs Ingestion Endpoints

| File | Has Ingestion Endpoint? | Target Table |
|---|---|---|
| `peak_ccu_history.json` | Done - `POST /api/ingestion/peak-ccu` | ccu_history |
| `regional_sales_history.json` | Done - `POST /api/ingestion/financial` | steam_sale_financial |
| `steamworks_wishlists.csv` | Done - `POST /api/ingestion/wishlists` | owned_game_data |
| `group_member_count.json` | Done - via ingestion service | group_member_count_snapshot |
| `store_details.json` | Done - via game enrichment | main_game + game_genre + genre_tag |
| `wishlist_activity.json` | Partial - similar to wishlists | owned_game_data |
| `wishlist_insights.json` | **Missing** - no endpoint | owned_game_data (needs mapping) |
| `regional_price_history.json` | **Missing** - no endpoint | price_snapshot (needs regional support) |
| `active_users_history.json` | **Missing** - no endpoint | ccu_history or new table |
| `active_users_regions.json` | **Missing** - no endpoint | needs new table |
| `achievements.json` | **Missing** - no endpoint | needs new table |
| `news.json` | **Missing** - no endpoint | needs new table |
| `patch_notes.json` | **Missing** - no endpoint | needs new table |
| `recent_reviews.json` | **Missing** - no endpoint | needs new table or enrich main_game |
| `change_history.json` | Done - via PICS change tracking | app_change_record |

---

## 5. PRIORITY ACTIONS

### High Priority (feeds existing tables, just needs ingestion endpoints)

1. **Store Traffic CSV ingestion** - Table exists, entity exists, just needs `POST /api/ingestion/traffic` endpoint (like peak-ccu pattern)
2. **Regional Price CSV ingestion** - PriceSnapshot entity exists, needs regional CSV upload
3. **Active Users History ingestion** - Maps to ccu_history, file exists in Data Pulled/
4. **Wishlist Insights ingestion** - Maps to owned_game_data, file exists

### Medium Priority (needs new tables + endpoints)

5. **Marketing Module** (Action, GameAction, ActionTarget, ActionImpact, TargetMatch) - Manual entry, tables in schema but no entities/handlers
6. **Game Demo Table** - New entity + enrichment from Steam API + CSV upload for demo downloads
7. **News/Patch Notes Table** - API data already in Data Pulled/, needs entity + ingestion

### Lower Priority (enrichment/new features)

8. **User/Influencer Table** - IPlayerService integration
9. **Achievements Table** - Global achievement stats
10. **Reviews ingestion** - Enrich review data beyond count/score
11. **Bundle Sales ingestion** - Separate from regular sales
12. **Discount History scraping** - Feeds ACTION table (once marketing module exists)
