ALTER TABLE boards RENAME COLUMN cells TO original_cells;
ALTER TABLE boards DROP CONSTRAINT boards_cells_shape;
ALTER TABLE boards ADD COLUMN cells jsonb;

-- WITH ORDINALITY preserves row-major order, including legacy arrays whose lower bound was not 1.
UPDATE boards AS b SET cells = (
    SELECT jsonb_agg(row_cells ORDER BY row_number)
    FROM (
        SELECT (ordinality - 1) / b.column_count AS row_number,
               jsonb_agg(CASE WHEN cell THEN 1 ELSE 0 END ORDER BY ordinality) AS row_cells
        FROM unnest(b.original_cells) WITH ORDINALITY AS c(cell, ordinality)
        GROUP BY (ordinality - 1) / b.column_count
    ) AS rows
);

ALTER TABLE boards
    DROP COLUMN original_cells,
    ALTER COLUMN cells SET NOT NULL,
    ADD COLUMN generation bigint NOT NULL DEFAULT 0 CHECK (generation >= 0),
    ADD COLUMN status text NOT NULL DEFAULT 'Active' CHECK (status IN ('Active', 'Stable', 'Cycle')),
    ADD COLUMN updated_at timestamptz,
    ADD COLUMN completed_at timestamptz,
    ADD COLUMN cycle_start_generation bigint,
    ADD COLUMN cycle_length integer;

UPDATE boards SET updated_at = created_at;
ALTER TABLE boards ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE boards ALTER COLUMN updated_at SET DEFAULT now();

ALTER TABLE boards ADD CONSTRAINT boards_cells_shape CHECK (
    CASE WHEN jsonb_typeof(cells) = 'array' THEN jsonb_array_length(cells) = row_count ELSE false END
);
ALTER TABLE boards ADD CONSTRAINT boards_terminal_state CHECK (
    (status = 'Active' AND completed_at IS NULL AND cycle_start_generation IS NULL AND cycle_length IS NULL)
    OR
    (status IN ('Stable', 'Cycle') AND completed_at IS NOT NULL
     AND cycle_start_generation IS NOT NULL AND cycle_length IS NOT NULL
     AND cycle_start_generation >= 0 AND cycle_start_generation < generation
     AND generation - cycle_start_generation = cycle_length
     AND ((status = 'Stable' AND cycle_length = 1) OR (status = 'Cycle' AND cycle_length > 1)))
);
