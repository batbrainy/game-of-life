CREATE TABLE boards (
    id           uuid        PRIMARY KEY,
    row_count    integer     NOT NULL CHECK (row_count > 0),
    column_count integer     NOT NULL CHECK (column_count > 0),
    cells        boolean[]   NOT NULL,
    created_at   timestamptz NOT NULL DEFAULT now(),
    -- array_position(cells, NULL) is NULL only when no cell is NULL. The bigint cast keeps
    -- row_count * column_count from failing with "integer out of range" for large dimensions.
    CONSTRAINT boards_cells_shape CHECK (
        array_ndims(cells) = 1
        AND cardinality(cells) = row_count::bigint * column_count
        AND array_position(cells, NULL) IS NULL)
);
