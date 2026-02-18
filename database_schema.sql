-- CATO Backend Database Schema
-- PostgreSQL 14+
-- Last Updated: February 6, 2026
--
-- This schema implements the complete MVP data structure from CATO Data Yapısı specification
-- Use this as reference when creating Entity Framework Core models

-- ============================================================================
-- EXTENSIONS
-- ============================================================================

-- Enable UUID generation
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- ============================================================================
-- CORE GAME TABLES
-- ============================================================================

-- Legal entities (developers, publishers)
CREATE TABLE legal_entity (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(500) NOT NULL,
    entity_type VARCHAR(50) NOT NULL CHECK (entity_type IN ('Developer', 'Publisher')),
    contact_email VARCHAR(255),
    website TEXT,
    notes TEXT,
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_legal_entity_type ON legal_entity(entity_type);
CREATE INDEX idx_legal_entity_name ON legal_entity(name);

-- Main game catalog (owned, competitors, sourcing)
CREATE TABLE main_game (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    app_id INTEGER UNIQUE NOT NULL,
    name VARCHAR(500) NOT NULL,
    game_type VARCHAR(50) NOT NULL CHECK (game_type IN ('Owned', 'Competitor', 'Sourcing')),
    release_date DATE,
    price_usd DECIMAL(10,2),
    discount_percent INTEGER DEFAULT 0 CHECK (discount_percent >= 0 AND discount_percent <= 100),
    developer_id UUID REFERENCES legal_entity(id),
    publisher_id UUID REFERENCES legal_entity(id),
    is_early_access BOOLEAN DEFAULT FALSE,
    is_released BOOLEAN DEFAULT FALSE,
    header_image_url TEXT,
    capsule_image_url TEXT,
    short_description TEXT,
    detailed_description TEXT,
    website TEXT,
    platforms JSONB, -- {"windows": true, "mac": false, "linux": false}
    supported_languages TEXT,
    metacritic_score INTEGER CHECK (metacritic_score >= 0 AND metacritic_score <= 100),
    steam_review_score VARCHAR(50), -- "Overwhelmingly Positive", etc.
    review_count INTEGER DEFAULT 0,
    followers_count INTEGER DEFAULT 0, -- Steam community group followers
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_main_game_app_id ON main_game(app_id);
CREATE INDEX idx_main_game_type ON main_game(game_type);
CREATE INDEX idx_main_game_release_date ON main_game(release_date);
CREATE INDEX idx_main_game_developer ON main_game(developer_id);
CREATE INDEX idx_main_game_publisher ON main_game(publisher_id);

-- Genre assignments (Steam and internal)
CREATE TABLE game_genre (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    game_id UUID NOT NULL REFERENCES main_game(id) ON DELETE CASCADE,
    genre_name VARCHAR(200) NOT NULL,
    genre_type VARCHAR(50) NOT NULL CHECK (genre_type IN ('Primary', 'Secondary')),
    source VARCHAR(50) NOT NULL CHECK (source IN ('Steam', 'Internal')),
    created_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_game_genre_game_id ON game_genre(game_id);
CREATE INDEX idx_game_genre_name ON game_genre(genre_name);
CREATE UNIQUE INDEX idx_game_genre_unique ON game_genre(game_id, genre_name, source);

-- Detailed tag system (mechanics, themes, mood)
CREATE TABLE genre_tag (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    game_id UUID NOT NULL REFERENCES main_game(id) ON DELETE CASCADE,
    tag_name VARCHAR(200) NOT NULL,
    tag_type VARCHAR(50) NOT NULL CHECK (tag_type IN ('Genre', 'Subgenre', 'Mechanic', 'Theme', 'Mood')),
    weight INTEGER DEFAULT 0 CHECK (weight >= 0), -- Relevance score from Steam (0-100)
    source VARCHAR(50) DEFAULT 'Steam' CHECK (source IN ('Steam', 'Internal', 'Manual')),
    created_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_genre_tag_game_id ON genre_tag(game_id);
CREATE INDEX idx_genre_tag_name ON genre_tag(tag_name);
CREATE INDEX idx_genre_tag_type ON genre_tag(tag_type);
CREATE UNIQUE INDEX idx_genre_tag_unique ON genre_tag(game_id, tag_name);

-- ============================================================================
-- FINANCIAL & TRAFFIC TABLES
-- ============================================================================

-- Steam Partner financial data (detailed sales)
CREATE TABLE steam_sale_financial (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    game_id UUID NOT NULL REFERENCES main_game(id) ON DELETE CASCADE,
    sale_date DATE NOT NULL,
    country_code VARCHAR(10) NOT NULL,
    platform VARCHAR(50) DEFAULT 'Steam', -- 'Steam', 'Windows', 'Mac', 'Linux'
    package_id INTEGER, -- Steam package ID
    sales_units INTEGER DEFAULT 0,
    returns_units INTEGER DEFAULT 0,
    net_units INTEGER GENERATED ALWAYS AS (sales_units - returns_units) STORED,
    gross_revenue_usd DECIMAL(15,2) DEFAULT 0,
    gross_returns_usd DECIMAL(15,2) DEFAULT 0,
    steam_commission_usd DECIMAL(15,2) DEFAULT 0,
    tax_usd DECIMAL(15,2) DEFAULT 0,
    net_revenue_usd DECIMAL(15,2) DEFAULT 0,
    currency VARCHAR(10),
    base_price VARCHAR(20), -- Original price as string (e.g. "19.99")
    sale_price VARCHAR(20), -- Discounted price as string
    discount_id INTEGER, -- Steam discount/promotion ID
    sale_type VARCHAR(50), -- 'Normal', 'Steam Sale', 'Publisher Sale'
    combined_discount_id INTEGER,
    revenue_share_tier INTEGER, -- Steam's revenue share tier
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW(),
    CONSTRAINT unique_sale_record UNIQUE(game_id, sale_date, country_code, package_id, platform)
);

CREATE INDEX idx_steam_sale_game_date ON steam_sale_financial(game_id, sale_date DESC);
CREATE INDEX idx_steam_sale_country ON steam_sale_financial(country_code);
CREATE INDEX idx_steam_sale_date ON steam_sale_financial(sale_date DESC);
CREATE INDEX idx_steam_sale_net_revenue ON steam_sale_financial(net_revenue_usd DESC);

-- Steam store traffic data
CREATE TABLE steam_traffic (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    game_id UUID NOT NULL REFERENCES main_game(id) ON DELETE CASCADE,
    traffic_date DATE NOT NULL,
    store_page_visits INTEGER DEFAULT 0,
    unique_visitors INTEGER DEFAULT 0,
    impressions INTEGER DEFAULT 0,
    click_through_rate DECIMAL(5,2) DEFAULT 0 CHECK (click_through_rate >= 0 AND click_through_rate <= 100),
    wishlist_additions INTEGER DEFAULT 0,
    wishlist_deletions INTEGER DEFAULT 0,
    net_wishlist_change INTEGER GENERATED ALWAYS AS (wishlist_additions - wishlist_deletions) STORED,
    purchases INTEGER DEFAULT 0,
    purchase_conversion_rate DECIMAL(5,2) DEFAULT 0 CHECK (purchase_conversion_rate >= 0 AND purchase_conversion_rate <= 100),
    traffic_source VARCHAR(100), -- 'Discovery Queue', 'Search', 'Event', 'Direct', 'External'
    created_at TIMESTAMP DEFAULT NOW(),
    CONSTRAINT unique_traffic_record UNIQUE(game_id, traffic_date, traffic_source)
);

CREATE INDEX idx_steam_traffic_game_date ON steam_traffic(game_id, traffic_date DESC);
CREATE INDEX idx_steam_traffic_source ON steam_traffic(traffic_source);
CREATE INDEX idx_steam_traffic_conversion ON steam_traffic(purchase_conversion_rate DESC);

-- ============================================================================
-- COMPETITOR TRACKING TABLES
-- ============================================================================

-- CCU (Concurrent Users) history - collected every 15 minutes
CREATE TABLE ccu_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    game_id UUID NOT NULL REFERENCES main_game(id) ON DELETE CASCADE,
    timestamp TIMESTAMP NOT NULL,
    ccu_count INTEGER NOT NULL CHECK (ccu_count >= 0),
    peak_ccu_today INTEGER, -- Peak CCU so far today (optional)
    source VARCHAR(50) DEFAULT 'Steam API' CHECK (source IN ('Steam API', 'SteamDB', 'Gamalytic', 'Manual')),
    created_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_ccu_history_game_time ON ccu_history(game_id, timestamp DESC);
CREATE INDEX idx_ccu_history_timestamp ON ccu_history(timestamp DESC);
CREATE UNIQUE INDEX idx_ccu_history_unique ON ccu_history(game_id, timestamp, source);

-- Wishlist rank tracking (from SteamDB, daily)
CREATE TABLE wishlist_rank_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    game_id UUID NOT NULL REFERENCES main_game(id) ON DELETE CASCADE,
    rank_date DATE NOT NULL,
    rank_position INTEGER CHECK (rank_position > 0),
    wishlists_count INTEGER, -- If available from source
    rank_change INTEGER, -- Change from previous day (calculated)
    source VARCHAR(50) DEFAULT 'SteamDB' CHECK (source IN ('SteamDB', 'Steamworks', 'Gamalytic')),
    created_at TIMESTAMP DEFAULT NOW(),
    CONSTRAINT unique_wishlist_rank UNIQUE(game_id, rank_date, source)
);

CREATE INDEX idx_wishlist_rank_game_date ON wishlist_rank_history(game_id, rank_date DESC);
CREATE INDEX idx_wishlist_rank_position ON wishlist_rank_history(rank_position);
CREATE INDEX idx_wishlist_rank_date ON wishlist_rank_history(rank_date DESC);

-- ============================================================================
-- MARKETING TABLES
-- ============================================================================

-- Marketing targets (influencers, events, media)
CREATE TABLE marketing_target (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(500) NOT NULL,
    target_type VARCHAR(50) NOT NULL CHECK (target_type IN ('Influencer', 'Media', 'Event', 'MailingList')),
    contact_email VARCHAR(255),
    contact_twitter VARCHAR(255),
    contact_discord VARCHAR(255),
    preferred_genres JSONB, -- ["FPS", "Strategy"]
    preferred_tags JSONB, -- ["Roguelike", "Co-op"]
    audience_size INTEGER CHECK (audience_size >= 0),
    audience_region VARCHAR(100), -- 'North America', 'Europe', 'Global', etc.
    platform VARCHAR(100), -- 'Twitch', 'YouTube', 'Twitter', 'Event', etc.
    engagement_rate DECIMAL(5,2) CHECK (engagement_rate >= 0 AND engagement_rate <= 100), -- For influencers
    cost_estimate_usd DECIMAL(15,2), -- Estimated cost for engagement
    last_contacted DATE,
    response_rate DECIMAL(5,2) CHECK (response_rate >= 0 AND response_rate <= 100),
    notes TEXT,
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_marketing_target_type ON marketing_target(target_type);
CREATE INDEX idx_marketing_target_name ON marketing_target(name);
CREATE INDEX idx_marketing_target_platform ON marketing_target(platform);

-- Marketing actions (campaigns)
CREATE TABLE action (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    action_type VARCHAR(50) NOT NULL CHECK (action_type IN ('Mailing', 'Influencer', 'Event', 'Discount', 'Bundle', 'PR', 'Advertisement')),
    decision_source VARCHAR(50) DEFAULT 'Manual' CHECK (decision_source IN ('Manual', 'Rule', 'AI', 'Automated')),
    status VARCHAR(50) DEFAULT 'Planned' CHECK (status IN ('Planned', 'Outreach', 'Negotiating', 'Scheduled', 'Executed', 'Completed', 'Cancelled', 'Failed')),
    planned_date DATE,
    action_date DATE, -- When the action was actually executed
    completion_date DATE, -- When results were finalized
    description TEXT NOT NULL,
    budget_usd DECIMAL(15,2),
    actual_cost_usd DECIMAL(15,2),
    notes TEXT,
    created_by VARCHAR(255), -- User who created the action
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_action_type ON action(action_type);
CREATE INDEX idx_action_status ON action(status);
CREATE INDEX idx_action_date ON action(action_date DESC);
CREATE INDEX idx_action_planned_date ON action(planned_date DESC);

-- Game-Action relationship (many-to-many)
CREATE TABLE game_action (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    action_id UUID NOT NULL REFERENCES action(id) ON DELETE CASCADE,
    game_id UUID NOT NULL REFERENCES main_game(id) ON DELETE CASCADE,
    game_role VARCHAR(50) DEFAULT 'Primary' CHECK (game_role IN ('Primary', 'Secondary', 'Featured', 'Included')),
    notes TEXT,
    created_at TIMESTAMP DEFAULT NOW(),
    CONSTRAINT unique_game_action UNIQUE(action_id, game_id)
);

CREATE INDEX idx_game_action_action ON game_action(action_id);
CREATE INDEX idx_game_action_game ON game_action(game_id);

-- Action-Target relationship (who was contacted)
CREATE TABLE action_target (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    action_id UUID NOT NULL REFERENCES action(id) ON DELETE CASCADE,
    target_id UUID NOT NULL REFERENCES marketing_target(id) ON DELETE CASCADE,
    outreach_date DATE,
    response_date DATE,
    status VARCHAR(50) DEFAULT 'Planned' CHECK (status IN ('Planned', 'Contacted', 'Responded', 'Accepted', 'Rejected', 'Negotiating', 'Live', 'Completed', 'Cancelled')),
    deliverable_url TEXT, -- Link to video, article, stream, etc.
    views INTEGER, -- Views/impressions for the deliverable
    engagement INTEGER, -- Likes, comments, etc.
    cost_usd DECIMAL(15,2),
    notes TEXT,
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW(),
    CONSTRAINT unique_action_target UNIQUE(action_id, target_id)
);

CREATE INDEX idx_action_target_action ON action_target(action_id);
CREATE INDEX idx_action_target_target ON action_target(target_id);
CREATE INDEX idx_action_target_status ON action_target(status);

-- Pre-calculated target matching scores
CREATE TABLE target_match (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    game_id UUID NOT NULL REFERENCES main_game(id) ON DELETE CASCADE,
    target_id UUID NOT NULL REFERENCES marketing_target(id) ON DELETE CASCADE,
    lifecycle_stage VARCHAR(50) CHECK (lifecycle_stage IN ('Pre-launch', 'Launch', 'Early Access', 'Post-launch', 'Live', 'Sunset')),
    relevance_score DECIMAL(5,2) DEFAULT 0 CHECK (relevance_score >= 0 AND relevance_score <= 100),
    genre_match_score DECIMAL(5,2) DEFAULT 0 CHECK (genre_match_score >= 0 AND genre_match_score <= 100),
    tag_match_score DECIMAL(5,2) DEFAULT 0 CHECK (tag_match_score >= 0 AND tag_match_score <= 100),
    historical_performance_score DECIMAL(5,2) DEFAULT 0 CHECK (historical_performance_score >= 0 AND historical_performance_score <= 100),
    sample_size INTEGER DEFAULT 0, -- How many past actions this is based on
    matching_genres JSONB, -- ["FPS", "Action"]
    matching_tags JSONB, -- ["Multiplayer", "Co-op"]
    calculated_at TIMESTAMP DEFAULT NOW(),
    CONSTRAINT unique_target_match UNIQUE(game_id, target_id, lifecycle_stage)
);

CREATE INDEX idx_target_match_game ON target_match(game_id);
CREATE INDEX idx_target_match_target ON target_match(target_id);
CREATE INDEX idx_target_match_score ON target_match(relevance_score DESC);
CREATE INDEX idx_target_match_stage ON target_match(lifecycle_stage);

-- Measured impact of actions
CREATE TABLE action_impact (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    action_id UUID NOT NULL REFERENCES action(id) ON DELETE CASCADE,
    measurement_start DATE NOT NULL,
    measurement_end DATE NOT NULL,
    baseline_start DATE, -- Start of baseline period (e.g., 7 days before)
    baseline_end DATE, -- End of baseline period
    
    -- Wishlist impact
    baseline_wishlist_adds INTEGER DEFAULT 0,
    result_wishlist_adds INTEGER DEFAULT 0,
    wishlist_change INTEGER DEFAULT 0,
    wishlist_change_percent DECIMAL(10,2),
    
    -- Sales impact
    baseline_sales_units INTEGER DEFAULT 0,
    result_sales_units INTEGER DEFAULT 0,
    sales_units_change INTEGER DEFAULT 0,
    sales_change_percent DECIMAL(10,2),
    
    -- Revenue impact
    baseline_revenue_usd DECIMAL(15,2) DEFAULT 0,
    result_revenue_usd DECIMAL(15,2) DEFAULT 0,
    revenue_change_usd DECIMAL(15,2) DEFAULT 0,
    revenue_change_percent DECIMAL(10,2),
    
    -- Traffic impact
    baseline_traffic INTEGER DEFAULT 0,
    result_traffic INTEGER DEFAULT 0,
    traffic_change INTEGER DEFAULT 0,
    traffic_change_percent DECIMAL(10,2),
    
    -- Conversion impact
    baseline_conversion_rate DECIMAL(5,2) DEFAULT 0,
    result_conversion_rate DECIMAL(5,2) DEFAULT 0,
    conversion_rate_change DECIMAL(5,2) DEFAULT 0,
    
    -- ROI calculation
    total_cost_usd DECIMAL(15,2), -- From action.actual_cost_usd
    roi DECIMAL(10,2), -- (revenue_change_usd - total_cost_usd) / total_cost_usd * 100
    
    notes TEXT,
    calculated_at TIMESTAMP DEFAULT NOW(),
    calculated_by VARCHAR(255), -- System or user who triggered calculation
    CONSTRAINT unique_action_impact UNIQUE(action_id)
);

CREATE INDEX idx_action_impact_action ON action_impact(action_id);
CREATE INDEX idx_action_impact_roi ON action_impact(roi DESC);
CREATE INDEX idx_action_impact_revenue_change ON action_impact(revenue_change_usd DESC);

-- ============================================================================
-- SYSTEM TABLES
-- ============================================================================

-- Data ingestion logs (track Python collector runs)
CREATE TABLE ingestion_log (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    source VARCHAR(100) NOT NULL, -- 'steam_financial', 'steamdb', 'gamalytic', etc.
    start_time TIMESTAMP NOT NULL,
    end_time TIMESTAMP,
    status VARCHAR(50) NOT NULL CHECK (status IN ('Running', 'Success', 'Failed', 'PartialSuccess')),
    records_processed INTEGER DEFAULT 0,
    records_inserted INTEGER DEFAULT 0,
    records_updated INTEGER DEFAULT 0,
    records_failed INTEGER DEFAULT 0,
    error_message TEXT,
    file_path TEXT, -- Source file path if file-based ingestion
    created_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_ingestion_log_source ON ingestion_log(source);
CREATE INDEX idx_ingestion_log_start_time ON ingestion_log(start_time DESC);
CREATE INDEX idx_ingestion_log_status ON ingestion_log(status);

-- Background job execution history (supplement Hangfire)
CREATE TABLE job_execution (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_name VARCHAR(200) NOT NULL,
    job_type VARCHAR(100) NOT NULL, -- 'CCU_Tracking', 'Daily_Ingestion', etc.
    start_time TIMESTAMP NOT NULL,
    end_time TIMESTAMP,
    status VARCHAR(50) NOT NULL CHECK (status IN ('Running', 'Success', 'Failed')),
    duration_ms INTEGER, -- Execution time in milliseconds
    error_message TEXT,
    metadata JSONB, -- Additional context (e.g., games processed, errors encountered)
    created_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_job_execution_name ON job_execution(job_name);
CREATE INDEX idx_job_execution_type ON job_execution(job_type);
CREATE INDEX idx_job_execution_start ON job_execution(start_time DESC);
CREATE INDEX idx_job_execution_status ON job_execution(status);

-- ============================================================================
-- VIEWS (for common queries)
-- ============================================================================

-- View: Latest CCU for each game
CREATE OR REPLACE VIEW latest_ccu AS
SELECT DISTINCT ON (game_id)
    game_id,
    timestamp,
    ccu_count,
    source
FROM ccu_history
ORDER BY game_id, timestamp DESC;

-- View: Latest wishlist rank for each game
CREATE OR REPLACE VIEW latest_wishlist_rank AS
SELECT DISTINCT ON (game_id)
    game_id,
    rank_date,
    rank_position,
    wishlists_count,
    source
FROM wishlist_rank_history
ORDER BY game_id, rank_date DESC;

-- View: Game summary with latest metrics
CREATE OR REPLACE VIEW game_summary AS
SELECT
    g.id,
    g.app_id,
    g.name,
    g.game_type,
    g.release_date,
    g.price_usd,
    g.is_released,
    g.is_early_access,
    le_dev.name AS developer_name,
    le_pub.name AS publisher_name,
    lc.ccu_count AS latest_ccu,
    lc.timestamp AS latest_ccu_timestamp,
    lwr.rank_position AS latest_wishlist_rank,
    lwr.rank_date AS latest_rank_date,
    g.followers_count,
    g.review_count,
    g.steam_review_score
FROM main_game g
LEFT JOIN legal_entity le_dev ON g.developer_id = le_dev.id
LEFT JOIN legal_entity le_pub ON g.publisher_id = le_pub.id
LEFT JOIN latest_ccu lc ON g.id = lc.game_id
LEFT JOIN latest_wishlist_rank lwr ON g.id = lwr.game_id;

-- View: Action performance summary
CREATE OR REPLACE VIEW action_performance AS
SELECT
    a.id AS action_id,
    a.action_type,
    a.action_date,
    a.status,
    a.budget_usd,
    a.actual_cost_usd,
    COUNT(DISTINCT ga.game_id) AS games_count,
    COUNT(DISTINCT at.target_id) AS targets_count,
    ai.revenue_change_usd,
    ai.sales_units_change,
    ai.wishlist_change,
    ai.roi,
    ai.calculated_at AS impact_calculated_at
FROM action a
LEFT JOIN game_action ga ON a.id = ga.action_id
LEFT JOIN action_target at ON a.id = at.action_id
LEFT JOIN action_impact ai ON a.id = ai.action_id
GROUP BY a.id, ai.id;

-- ============================================================================
-- FUNCTIONS
-- ============================================================================

-- Function: Update updated_at timestamp automatically
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Apply update_updated_at trigger to relevant tables
CREATE TRIGGER update_main_game_updated_at BEFORE UPDATE ON main_game
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_legal_entity_updated_at BEFORE UPDATE ON legal_entity
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_marketing_target_updated_at BEFORE UPDATE ON marketing_target
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_action_updated_at BEFORE UPDATE ON action
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_action_target_updated_at BEFORE UPDATE ON action_target
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_steam_sale_financial_updated_at BEFORE UPDATE ON steam_sale_financial
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- ============================================================================
-- SAMPLE DATA (optional, for testing)
-- ============================================================================

-- Insert sample legal entities
INSERT INTO legal_entity (name, entity_type) VALUES
    ('Valve Corporation', 'Developer'),
    ('Valve', 'Publisher'),
    ('Catoptric Games', 'Developer'),
    ('Catoptric Games', 'Publisher')
ON CONFLICT DO NOTHING;

-- ============================================================================
-- PERMISSIONS (adjust as needed)
-- ============================================================================

-- Grant permissions to application user
-- GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO cato_user;
-- GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO cato_user;

-- ============================================================================
-- NOTES
-- ============================================================================

-- 1. This schema uses UUIDs for primary keys (better for distributed systems)
-- 2. All foreign keys use ON DELETE CASCADE or appropriate actions
-- 3. Indexes are created for common query patterns (game_id, dates, types)
-- 4. JSONB is used for flexible data (platforms, genres, tags)
-- 5. CHECK constraints ensure data integrity
-- 6. Views provide convenient access to common queries
-- 7. Triggers automatically update `updated_at` timestamps
-- 8. UNIQUE constraints prevent duplicate data (e.g., same game + date)

-- ============================================================================
-- MAINTENANCE QUERIES
-- ============================================================================

-- Check database size
-- SELECT pg_size_pretty(pg_database_size('cato'));

-- Check table sizes
-- SELECT
--     schemaname,
--     tablename,
--     pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS size
-- FROM pg_tables
-- WHERE schemaname = 'public'
-- ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;

-- Vacuum and analyze (maintenance)
-- VACUUM ANALYZE;
