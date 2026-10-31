<script setup lang="ts" generic="Row extends object">
import { computed, useId, useTemplateRef, type ComputedRef, type Ref } from 'vue'
import { useHorizontalOverflow } from '../../composables/useHorizontalOverflow'
import { useMediaQuery } from '../../composables/useMediaQuery'
import UiSkeleton from './UiSkeleton.vue'
import UiTableCell, { type CellPlacement, type CellTone } from './UiTableCell.vue'
import UiTableHeaderCell from './UiTableHeaderCell.vue'
import UiTableRow from './UiTableRow.vue'
import UiTableSortBar from './UiTableSortBar.vue'
import type { CellAlign, ColumnSort, UiTableColumn, UiTableColumnKind } from '../../types/tableColumn'

/**
 * The panel's only table primitive. Renders a real `<table>` with a `<caption>` plus `<thead>` /
 * `<tbody>` structure (rules/vue.md: UI comes from `components/ui`).
 *
 * **The column schema is the one description of a table.** A caller passes `columns` (an array of
 * {@link UiTableColumn}) and `rows`; the table draws the header row from the schema, with sorting
 * wired through `sort` and `@sort`, and each body cell from the caller's scoped slot
 * `#cell-<key>="{ row }"`. A column's `kind` decides its cell's face, tone, alignment and wrapping
 * (see {@link UiTableColumnKind}), so those are not restated per page.
 *
 * **Loading keeps the real header.** With `loading`, the header row is the real one and the body
 * is `skeletonRows` rows of {@link UiSkeleton} `cell`s, one per column by its `kind`. Nothing else
 * describes the table's loading shape, so it cannot drift from the loaded table. The placeholder
 * rows are `aria-hidden` and the table is `aria-busy`; the announcement belongs to the
 * {@link UiLoadingRegion} around it, which also keeps the placeholders invisible for a fast answer.
 *
 * **The caption is visually hidden but never removed.** Most screens already show a heading above
 * their table, so a second visible title would only repeat it, yet the caption is what announces
 * the table's purpose to a screen reader that lands on it from a landmark list. `sr-only` keeps it
 * in the accessibility tree and out of the layout.
 *
 * **The table scrolls inside its own container, never the page.** A wide table (the backups list
 * has eight columns) is wider than a 390 px screen. If it were allowed to size the page, the whole
 * content panel would scroll sideways and drag the app bar's context with it. The wrapper is the
 * scroll container instead, so the overflow is contained and the page itself stays put. The wrapper
 * must stay the table's DIRECT parent: a row menu that is teleported to `body` is measured against
 * it, and the e2e specs for the backups and databases lists look it up as `table.parentElement`.
 *
 * **Columns keep a readable width; the table never squashes.** The overflow only happens if the
 * table is allowed to be wider than its wrapper, and that needs every column to have a minimum
 * width above one character. Headings and badges do not wrap and {@link UiTableCell} carries a
 * floor width, so a table with many columns grows past a narrow wrapper and scrolls inside it.
 *
 * **Flat on purpose.** The approved design draws a table as hairlines on the surface it sits on:
 * one rule under the header, one between rows, a tinted row on hover. There is no header band and
 * no zebra striping — a band is a second surface that has to be contrast-checked against every
 * header colour, and stripes fight the hover tint for the same job. The rules and the hover live on
 * {@link UiTableRow}; the header's type lives on {@link UiTableHeaderCell}.
 *
 * **An overflowing wrapper is a deliberate, labelled scroll region; a fitting one is nothing.**
 * Chrome makes a scroll container keyboard-focusable on its own, which left a wide table with an
 * unlabelled Tab stop (named after the whole table's text) drawn with the browser's thin default
 * outline instead of the design ring. The wrapper therefore says what it is: `role="region"` named
 * by the caption through `aria-labelledby`, and `tabindex="0"` so a keyboard user can scroll it with
 * the arrow keys. The stop exists ONLY while the content really overflows, measured with a
 * `ResizeObserver` on the wrapper and the table (a table can start or stop overflowing when its
 * rows, the language or the viewport change). A fitting table adds no Tab stop. The ring is the
 * design's `shadow-focus` (docs/design/redesign-2026-10/README.md: never rebuild it by hand) with
 * the outline removed; a shadow is drawn outside the wrapper's own box, which that box's `overflow`
 * does not clip, and the main region's padding leaves room for it.
 *
 * **An overflowing table says so, and says which way.** On a phone a cut-off column looks like the
 * end of the table. While content is hidden past an edge, that edge carries a 12px fade into the
 * table's surface, and the fades follow the scroll position: the left one appears once the user has
 * scrolled, the right one goes once they reach the end. They are decoration
 * (`pointer-events: none`, no ARIA): the region role above is what tells assistive technology.
 *
 * **The row action column stays on screen.** A last column aligned to the end (a row's actions
 * menu, by this kit's convention: {@link UiTableCell} `align="end"`) is `position: sticky` at the
 * right edge, painted with the surface colour so rows slide under it, and it carries a left shadow
 * while there is more table to its left hidden beneath it. Scrolling to read a long row never
 * scrolls away the control that acts on it.
 *
 * **Below `sm` every row is a card built from the same schema** (issue #217). A phone has no room
 * for a table worth scrolling sideways, and a scrolled-away column is a value nobody reads, so the
 * layout changes STRUCTURE there (which is why it is {@link useMediaQuery} and not CSS alone): the
 * row's title is its `identity` column (else its first `identifier`, else its first column), its
 * actions sit at the top right and are always in view, and every other column is a line labelled
 * with the column's heading. Nothing about it is written per page. The elements stay the table's
 * own, with their roles stated explicitly (`table`, `rowgroup`, `row`, `columnheader`, `cell`),
 * because a table element given another `display` loses its implicit role in some browsers; the
 * header row stays in the accessibility tree, visually hidden, so a cell is still announced with
 * its column. Sorting stays reachable through {@link UiTableSortBar}, derived from the sortable
 * columns, in place of the headings that cannot be pressed when they are not drawn. The loading
 * placeholders are drawn through the same cells, so they take the card's shape too. From `sm` up
 * the table is exactly the table described above.
 */

/** The fields of a row whose value can key it: those holding a string or a number. */
type RowKeyField<R> = { [K in keyof R]: R[K] extends string | number ? K : never }[keyof R]

/** Props accepted by {@link UiTable}. */
const props = withDefaults(
  defineProps<{
    /** Accessible name for the table, already translated by the caller. Visually hidden. */
    caption: string
    /** The column schema: the header, the cells and the placeholder rows all read it. */
    columns: readonly UiTableColumn[]
    /** The rows to render, one per record. */
    rows?: readonly Row[]
    /** The field of a row that identifies it (a string or a number), used as the row's key. */
    rowKey?: RowKeyField<Row>
    /** Whether the rows are still loading: the real header over placeholder rows. */
    loading?: boolean
    /** How many placeholder rows to draw while loading; pass the page size the rows will fill. */
    skeletonRows?: number
    /** The list's sort key, `-` prefixed when descending, empty for the server's default order. */
    sort?: string
  }>(),
  { rows: () => {return []}, rowKey: undefined, loading: false, skeletonRows: 5, sort: '' },
)

/** Events emitted by {@link UiTable}. */
const emit = defineEmits<{
  /** The reader asked to sort by a column, carrying that column's `sortField`. */
  (e: 'sort', field: string): void
}>()

/** Slots of {@link UiTable}. */
defineSlots<{
  /** One body cell of a schema column, given the row it belongs to. */
  [slot: `cell-${string}`]: ((scope: { row: Row }) => unknown) | undefined
}>()

/** The tone of each kind's cells. */
const KIND_TONES: Record<UiTableColumnKind, CellTone> = {
  identity: 'primary',
  identifier: 'primary',
  text: 'secondary',
  mono: 'secondary',
  code: 'primary',
  badge: 'primary',
  number: 'primary',
  date: 'muted',
  actions: 'primary',
}

/** The kinds set in the mono face with tabular digits. */
const MONO_KINDS: ReadonlySet<UiTableColumnKind> = new Set<UiTableColumnKind>([
  'identifier',
  'mono',
  'code',
  'number',
  'date',
])

/** The kinds set in the medium weight: what a row is scanned for. */
const STRONG_KINDS: ReadonlySet<UiTableColumnKind> = new Set<UiTableColumnKind>(['identity', 'identifier'])

/** The kinds whose cells wrap; every other kind keeps one line. */
const WRAP_KINDS: ReadonlySet<UiTableColumnKind> = new Set<UiTableColumnKind>(['text', 'code'])

/** The kinds that sit at the end of their column unless the column says otherwise. */
const END_KINDS: ReadonlySet<UiTableColumnKind> = new Set<UiTableColumnKind>(['number', 'actions'])

/** The width from which rows are a table; below it they are cards (`sm`, 40rem). */
const TABLE_QUERY = '(min-width: 40rem)'

/** Whether the window is wide enough for the table layout. */
const wide: Readonly<Ref<boolean>> = useMediaQuery(TABLE_QUERY)

/** Unique id tying the scroll region's accessible name to the caption. */
const captionId: string = useId()

/** The scroll wrapper, measured to decide whether it is a keyboard stop. */
const scroller = useTemplateRef<HTMLElement>('scroller')

/** The table inside it, observed so growth of the content (not only the viewport) is noticed. */
const tableElement = useTemplateRef<HTMLElement>('table')

/** The wrapper's overflow, which decides the keyboard stop and the edge fades. */
const { overflows, hiddenStart, hiddenEnd, onScroll } = useHorizontalOverflow(scroller, tableElement)

/** Whether the rows are drawn as cards. */
const cards: ComputedRef<boolean> = computed(() => {
  return !wide.value
})

/** Whether any column can be sorted, which is when the cards need their sort control. */
const sortable: ComputedRef<boolean> = computed(() => {
  return props.columns.some((column) => {
    return column.sortField !== undefined
  })
})

/** The key of the column a card is titled by: the identity, else the first identifier, else the first. */
const titleKey: ComputedRef<string | undefined> = computed(() => {
  const title =
    props.columns.find((column) => {
      return column.kind === 'identity'
    }) ??
    props.columns.find((column) => {
      return column.kind === 'identifier'
    }) ??
    props.columns[0]
  return title?.key
})

/** Whether the schema has an actions column, without which a card's title takes the whole width. */
const hasActions: ComputedRef<boolean> = computed(() => {
  return props.columns.some((column) => {
    return column.kind === 'actions'
  })
})

/** The placeholder row numbers drawn while loading. */
const placeholderRows: ComputedRef<number[]> = computed(() => {
  return Array.from({ length: props.skeletonRows }, (_, index) => {
    return index
  })
})

/**
 * Where a column's heading and cells sit.
 * @param column The column.
 * @returns The column's own alignment, or its kind's.
 */
const alignOf = (column: UiTableColumn): CellAlign => {
  return column.align ?? (END_KINDS.has(column.kind) ? 'end' : 'start')
}

/**
 * The order a sortable column is in, read from the list's sort key.
 * @param column The column.
 * @returns Ascending, descending or none for a sortable column; `undefined` for one that is not.
 */
const sortOf = (column: UiTableColumn): ColumnSort | undefined => {
  if (column.sortField === undefined) {
    return undefined
  }
  if (props.sort === column.sortField) {
    return 'ascending'
  }
  return props.sort === `-${column.sortField}` ? 'descending' : 'none'
}

/**
 * Reports a press on a sortable heading.
 * @param column The column whose heading was pressed.
 * @returns Nothing.
 */
const requestSort = (column: UiTableColumn): void => {
  if (column.sortField !== undefined) {
    emit('sort', column.sortField)
  }
}

/**
 * Which part of the row a column's cells are.
 * @param column The column.
 * @returns `column` in the table layout; the card's title, actions or a labelled line in cards.
 */
const placementOf = (column: UiTableColumn): CellPlacement => {
  if (!cards.value) {
    return 'column'
  }
  if (column.kind === 'actions') {
    return 'actions'
  }
  return column.key === titleKey.value ? 'title' : 'line'
}

/**
 * The key of a body row, read from the field the caller named.
 * @param row The row.
 * @param index The row's position, the fallback when no `rowKey` is given.
 * @returns A key unique among the rows.
 */
const keyOf = (row: Row, index: number): string => {
  return props.rowKey === undefined ? String(index) : String(row[props.rowKey])
}
</script>

<template>
  <!-- The outer box holds the cards' sort bar and positions the edge fades; the scroller stays the
       table's direct parent. -->
  <div class="relative flex w-full max-w-full flex-col gap-3">
    <UiTableSortBar v-if="cards && sortable" :columns="columns" :sort="sort" @sort="emit('sort', $event)" />
    <div
      ref="scroller"
      class="ui-table-scroller w-full max-w-full overflow-x-auto focus-visible:shadow-focus focus-visible:outline-none"
      :role="overflows ? 'region' : undefined"
      :aria-labelledby="overflows ? captionId : undefined"
      :tabindex="overflows ? 0 : undefined"
      :data-hidden-end="hiddenEnd ? 'true' : undefined"
      :data-layout="cards ? 'cards' : 'table'"
      data-testid="table-scroller"
      @scroll.passive="onScroll"
    >
      <table
        ref="table"
        class="w-full text-left text-sm"
        :class="cards ? 'block' : 'border-collapse'"
        :role="cards ? 'table' : undefined"
        :aria-busy="loading ? 'true' : undefined"
      >
        <caption :id="captionId" class="sr-only">{{ caption }}</caption>
        <thead :class="cards ? 'sr-only' : ''" :role="cards ? 'rowgroup' : undefined">
          <UiTableRow :role="cards ? 'row' : undefined">
            <UiTableHeaderCell
              v-for="column in columns"
              :key="column.key"
              :role="cards ? 'columnheader' : undefined"
              :align="alignOf(column)"
              :sort="cards ? undefined : sortOf(column)"
              @sort="requestSort(column)"
            >
              {{ column.label }}
            </UiTableHeaderCell>
          </UiTableRow>
        </thead>
        <tbody v-if="loading" :class="cards ? 'flex flex-col gap-2' : ''" :role="cards ? 'rowgroup' : undefined">
          <UiTableRow
            v-for="placeholder in placeholderRows"
            :key="placeholder"
            :card="cards"
            :role="cards ? 'row' : undefined"
            aria-hidden="true"
          >
            <UiTableCell
              v-for="column in columns"
              :key="column.key"
              :role="cards ? 'cell' : undefined"
              :align="alignOf(column)"
              :placement="placementOf(column)"
              :label="column.label"
              :wide="!hasActions"
              nowrap
            >
              <UiSkeleton shape="cell" :kind="column.kind" />
            </UiTableCell>
          </UiTableRow>
        </tbody>
        <tbody v-else :class="cards ? 'flex flex-col gap-2' : ''" :role="cards ? 'rowgroup' : undefined">
          <UiTableRow v-for="(row, index) in rows" :key="keyOf(row, index)" :card="cards" :role="cards ? 'row' : undefined">
            <UiTableCell
              v-for="column in columns"
              :key="column.key"
              :role="cards ? 'cell' : undefined"
              :align="alignOf(column)"
              :tone="KIND_TONES[column.kind]"
              :mono="MONO_KINDS.has(column.kind)"
              :strong="STRONG_KINDS.has(column.kind)"
              :nowrap="!WRAP_KINDS.has(column.kind)"
              :min-width="column.minWidth"
              :placement="placementOf(column)"
              :label="column.label"
              :wide="!hasActions"
            >
              <slot :name="`cell-${column.key}`" :row="row" />
            </UiTableCell>
          </UiTableRow>
        </tbody>
      </table>
    </div>
    <span
      v-if="hiddenStart"
      class="pointer-events-none absolute inset-y-0 left-0 w-3 bg-linear-to-r from-surface-1 to-transparent"
      data-testid="table-fade-start"
      aria-hidden="true"
    />
    <span
      v-if="hiddenEnd"
      class="pointer-events-none absolute inset-y-0 right-0 w-3 bg-linear-to-l from-surface-1 to-transparent"
      data-testid="table-fade-end"
      aria-hidden="true"
    />
  </div>
</template>

<style scoped>
/* The row action column: the last cell of every row when it is end-aligned. Sticky at the right
   edge, painted with the table's surface so the rows scrolling beneath do not show through. */
.ui-table-scroller[data-layout='table'] :deep(tr > [data-align='end']:last-child) {
  position: sticky;
  right: 0;
  z-index: 1;
  background-color: var(--s1);
}

.ui-table-scroller[data-layout='table'] :deep(tbody tr:hover > [data-align='end']:last-child) {
  background-color: var(--s2);
}

/* While columns are hidden beneath it, the sticky column casts a short shadow to its left so it
   reads as lying ON the table rather than being its last column. */
.ui-table-scroller[data-hidden-end='true'] :deep(tr > [data-align='end']:last-child)::before {
  content: '';
  position: absolute;
  inset-block: 0;
  left: -0.75rem;
  width: 0.75rem;
  pointer-events: none;
  background: linear-gradient(to left, color-mix(in srgb, var(--bg) 45%, transparent), transparent);
}
</style>
