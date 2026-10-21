-- AI assistant chat tables.
--
-- The conversational panel stores its conversations in the database rather than in
-- the browser, so a conversation survives a reload, is available on any device, and
-- the backend can cap how many turns are sent to the model.
--
-- This app uses EnsureCreated() rather than EF migrations. That creates the whole
-- schema when the database is empty, but it does nothing when tables already exist —
-- so these tables must be created deliberately on an existing database.
--
-- Safe to run more than once: the CREATE statements are guarded.

BEGIN;

CREATE TABLE IF NOT EXISTS "AiChatSessions" (
    "Id"              SERIAL PRIMARY KEY,
    "UserId"          integer NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "Title"           character varying(200) NOT NULL DEFAULT 'New conversation',
    "CreatedAt"       timestamp with time zone NOT NULL DEFAULT now(),
    "LastMessageAt"   timestamp with time zone NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS "AiChatMessages" (
    "Id"          SERIAL PRIMARY KEY,
    "SessionId"   integer NOT NULL REFERENCES "AiChatSessions"("Id") ON DELETE CASCADE,
    "Role"        character varying(20) NOT NULL DEFAULT 'user',
    "Content"     text NOT NULL,
    "CreatedAt"   timestamp with time zone NOT NULL DEFAULT now(),
    "UsedFallback" boolean NOT NULL DEFAULT false
);

-- Session list: always the user's own sessions, most recently active first.
CREATE INDEX IF NOT EXISTS "IX_AiChatSessions_UserId_LastMessageAt"
    ON "AiChatSessions" ("UserId", "LastMessageAt");

-- Reading a conversation in order.
CREATE INDEX IF NOT EXISTS "IX_AiChatMessages_SessionId_CreatedAt"
    ON "AiChatMessages" ("SessionId", "CreatedAt");

COMMIT;

-- Verify: both tables should be present.
-- ILIKE, not LIKE: the table names are mixed case ("AiChatSessions"), and LIKE is
-- case-sensitive in PostgreSQL.
SELECT tablename FROM pg_tables
WHERE schemaname = 'public' AND tablename ILIKE 'ai%'
ORDER BY tablename;
