namespace NTech.DataStudio.Storage.Sqlite.Migrations;

/// <summary>
/// Ordered schema scripts. The database's PRAGMA user_version is the number of
/// scripts applied, so released entries must never be edited, only appended.
/// </summary>
internal static class ProjectMigrations
{
    public static readonly IReadOnlyList<string> Scripts = new[]
    {
        """
        CREATE TABLE project_meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE source_images (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            original_path   TEXT NOT NULL,
            stored_path     TEXT NOT NULL,
            is_copied       INTEGER NOT NULL,
            file_size       INTEGER NOT NULL,
            modified_ticks  INTEGER NOT NULL,
            format          TEXT NOT NULL,
            width           INTEGER NOT NULL,
            height          INTEGER NOT NULL,
            bit_depth       INTEGER NOT NULL,
            channels        INTEGER NOT NULL,
            group_key       TEXT,
            sha256          TEXT,
            phash           INTEGER,
            dhash           INTEGER,
            blur_score      REAL,
            dark_fraction   REAL,
            bright_fraction REAL,
            reject_reason   TEXT CHECK (reject_reason IN
                            ('corrupt', 'exact_duplicate', 'near_duplicate', 'blurry', 'too_dark', 'too_bright')),
            reject_detail   TEXT,
            keep_override   INTEGER NOT NULL DEFAULT 0,
            UNIQUE (original_path, file_size, modified_ticks)
        );
        CREATE INDEX ix_source_images_sha256 ON source_images (sha256);

        CREATE TABLE images (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            source_image_id INTEGER NOT NULL REFERENCES source_images (id) ON DELETE RESTRICT,
            master_path     TEXT NOT NULL UNIQUE,
            thumb_path      TEXT NOT NULL,
            width           INTEGER NOT NULL,
            height          INTEGER NOT NULL,
            origin_x        INTEGER NOT NULL,
            origin_y        INTEGER NOT NULL,
            scale           REAL NOT NULL,
            file_size       INTEGER NOT NULL,
            status          TEXT NOT NULL DEFAULT 'new'
                            CHECK (status IN ('new', 'labeled', 'reviewed', 'rejected', 'null'))
        );
        CREATE INDEX ix_images_source ON images (source_image_id);
        CREATE INDEX ix_images_status ON images (status);

        -- Masters still in play: a rejected source takes its masters out without touching their labels.
        CREATE VIEW active_images AS
            SELECT images.* FROM images
            JOIN source_images ON source_images.id = images.source_image_id
            WHERE source_images.reject_reason IS NULL AND images.status <> 'rejected';

        CREATE TABLE classes (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            name       TEXT NOT NULL UNIQUE,
            color      TEXT NOT NULL,
            hotkey     INTEGER CHECK (hotkey BETWEEN 1 AND 9),
            sort_order INTEGER NOT NULL
        );

        CREATE TABLE annotations (
            id          TEXT PRIMARY KEY,
            image_id    INTEGER NOT NULL REFERENCES images (id) ON DELETE CASCADE,
            class_id    INTEGER NOT NULL REFERENCES classes (id) ON DELETE RESTRICT,
            kind        TEXT NOT NULL CHECK (kind IN ('box', 'polygon', 'obb', 'point')),
            points_json TEXT NOT NULL,
            source      TEXT NOT NULL CHECK (source IN ('manual', 'model')),
            created_at  TEXT NOT NULL,
            updated_at  TEXT NOT NULL
        );
        CREATE INDEX ix_annotations_image ON annotations (image_id);
        CREATE INDEX ix_annotations_class ON annotations (class_id);

        CREATE TABLE image_tags (
            image_id INTEGER NOT NULL REFERENCES images (id) ON DELETE CASCADE,
            class_id INTEGER NOT NULL REFERENCES classes (id) ON DELETE RESTRICT,
            PRIMARY KEY (image_id, class_id)
        );

        CREATE TABLE versions (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            name         TEXT NOT NULL UNIQUE,
            prep_json    TEXT NOT NULL,
            split_json   TEXT NOT NULL,
            augment_json TEXT NOT NULL,
            seed         INTEGER NOT NULL,
            created_at   TEXT NOT NULL
        );

        CREATE TABLE version_items (
            version_id INTEGER NOT NULL REFERENCES versions (id) ON DELETE CASCADE,
            image_id   INTEGER NOT NULL REFERENCES images (id) ON DELETE RESTRICT,
            split      TEXT NOT NULL CHECK (split IN ('train', 'val', 'test')),
            PRIMARY KEY (version_id, image_id)
        );
        """,
        // 2: images picked for the dataset. Off by default; it only counts while project_meta
        // 'dataset_selection_only' is '1'. The view is rebuilt because SELECT * freezes its columns.
        """
        ALTER TABLE images ADD COLUMN in_dataset INTEGER NOT NULL DEFAULT 0;

        DROP VIEW active_images;
        CREATE VIEW active_images AS
            SELECT images.* FROM images
            JOIN source_images ON source_images.id = images.source_image_id
            WHERE source_images.reject_reason IS NULL AND images.status <> 'rejected';
        """
    };
}
