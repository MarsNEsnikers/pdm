-- Схема Mini-PDM для SQLite. Выполняется при каждом старте (IF NOT EXISTS).

CREATE TABLE IF NOT EXISTS pdm_object (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    object_type        TEXT    NOT NULL CHECK (object_type IN ('Assembly', 'Part', 'StandardPart')),
    designation        TEXT    NULL,
    name               TEXT    NOT NULL,
    current_version_id INTEGER NULL,
    CHECK ((object_type = 'StandardPart' AND designation IS NULL)
        OR (object_type <> 'StandardPart' AND designation IS NOT NULL))
);

-- Обозначение уникально среди сборок и деталей; наименование среди стандартных изделий.
CREATE UNIQUE INDEX IF NOT EXISTS ux_pdm_object_designation
    ON pdm_object (designation) WHERE designation IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_pdm_object_standard_name
    ON pdm_object (name) WHERE object_type = 'StandardPart';

CREATE TABLE IF NOT EXISTS object_version (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    object_id  INTEGER NOT NULL REFERENCES pdm_object (id),
    version_no INTEGER NOT NULL CHECK (version_no > 0),
    state      TEXT    NOT NULL CHECK (state IN ('InWork', 'Approved', 'Annulled')),
    material   TEXT    NULL,
    mass_kg    REAL    NULL CHECK (mass_kg IS NULL OR mass_kg > 0),
    created_at TEXT    NOT NULL,
    UNIQUE (object_id, version_no)
);

CREATE TABLE IF NOT EXISTS bom_link (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    parent_version_id INTEGER NOT NULL REFERENCES object_version (id) ON DELETE CASCADE,
    child_object_id   INTEGER NOT NULL REFERENCES pdm_object (id),
    quantity          INTEGER NOT NULL CHECK (quantity > 0),
    UNIQUE (parent_version_id, child_object_id)
);
CREATE INDEX IF NOT EXISTS ix_bom_link_child ON bom_link (child_object_id);

CREATE TABLE IF NOT EXISTS import_log (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    started_at TEXT NOT NULL,
    file_name  TEXT NOT NULL,
    severity   TEXT NOT NULL,
    reason     TEXT NOT NULL
);
