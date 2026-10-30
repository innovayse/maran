// The column schema shared by `UiTable` (which draws a column's header and cells from it) and
// `UiSkeleton` (which draws a column's placeholder from its kind). It lives here rather than in
// `UiTable.vue` because `UiTable` renders `UiSkeleton`: declared in the table, the type would make
// the two components import each other.

/** How a cell's content sits in its column. */
export type CellAlign = 'start' | 'end'

/** The order a sortable column is in: by it ascending, by it descending, or not by it. */
export type ColumnSort = 'ascending' | 'descending' | 'none'

/**
 * What a column holds, which decides how its cells are set and how its placeholder is drawn:
 * - `identity`: what a row is scanned for, often beside an avatar (primary, medium weight, one line).
 * - `identifier`: what a row is scanned for when it is machine text, such as an address, a port or a
 *   database's full name (primary, medium weight, mono, one line).
 * - `text`: free text (secondary, wraps).
 * - `mono`: machine text such as a domain or a path (secondary, mono, one line).
 * - `code`: machine text long enough to need wrapping, such as a command line (primary, mono, wraps
 *   at its spaces, never mid-word).
 * - `badge`: a status pill (one line).
 * - `number`: a count or a size (mono, end-aligned, one line).
 * - `date`: a timestamp (muted, mono, one line).
 * - `actions`: the row's actions menu (end-aligned, so it stays on screen; one line).
 */
export type UiTableColumnKind =
  | 'identity'
  | 'identifier'
  | 'text'
  | 'mono'
  | 'code'
  | 'badge'
  | 'number'
  | 'date'
  | 'actions'

/** One column of a `UiTable`: the single description its header, cells and placeholder share. */
export interface UiTableColumn {
  /** Stable id of the column; its cells come from the caller's `#cell-<key>` slot. */
  key: string
  /** The heading, already translated by the caller. */
  label: string
  /** What the column holds; see {@link UiTableColumnKind}. */
  kind: UiTableColumnKind
  /** Overrides the kind's alignment (`number` and `actions` sit at the end, the rest at the start). */
  align?: CellAlign
  /** The server's sort key for this column; left out for a column that cannot be sorted. */
  sortField?: string
}
