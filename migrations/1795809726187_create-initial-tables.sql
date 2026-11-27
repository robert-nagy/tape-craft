-- Up Migration

-- Each feature that uses one of these tables adds its columns in its own migration.
CREATE TABLE users ();
CREATE TABLE tokens ();
CREATE TABLE tracks ();
CREATE TABLE analysis ();
CREATE TABLE playlists ();

-- Down Migration

DROP TABLE playlists;
DROP TABLE analysis;
DROP TABLE tracks;
DROP TABLE tokens;
DROP TABLE users;
