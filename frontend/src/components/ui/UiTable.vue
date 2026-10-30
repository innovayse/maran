<script setup lang="ts" generic="Row extends object">
import { computed, onBeforeUnmount, onMounted, ref, useId, useTemplateRef, type ComputedRef, type Ref } from 'vue'
import UiSkeleton from './UiSkeleton.vue'
import UiTableCell, { type CellTone } from './UiTableCell.vue'
import UiTableHeaderCell from './UiTableHeaderCell.vue'
import UiTableRow from './UiTableRow.vue'
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

/** Unique id tying the scroll region's accessible name to the caption. */
const captionId: string = useId()

/** The scroll wrapper, measured to decide whether it is a keyboard stop. */
const scroller = useTemplateRef<HTMLElement>('scroller')

/** The table inside it, observed so growth of the content (not only the viewport) is noticed. */
const tableElement = useTemplateRef<HTMLElement>('table')

/** Whether the content is wider than the wrapper right now. */
const overflows: Ref<boolean> = ref(false)

/** Whether content is hidden past the left edge (the user has scrolled). */
const hiddenStart: Ref<boolean> = ref(false)

/** Whether content is hidden past the right edge (more to scroll to). */
const hiddenEnd: Ref<boolean> = ref(false)

/** Observer that re-measures on any size change of the wrapper or the table. */
let observer: ResizeObserver | null = null

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
 * The key of a body row, read from the field the caller named.
 * @param row The row.
 * @param index The row's position, the fallback when no `rowKey` is given.
 * @returns A key unique among the rows.
 */
const keyOf = (row: Row, index: number): string => {
  return props.rowKey === undefined ? String(index) : String(row[props.rowKey])
}

/**
 * Re-reads which edges hide content, so the fades follow the scroll position. Uses the absolute
 * offset so a right-to-left layout, where `scrollLeft` runs negative, reads the same.
 * @returns Nothing; updates `hiddenStart` and `hiddenEnd`.
 */
const onScroll = (): void => {
  const element = scroller.value
  if (element === null || !overflows.value) {
    hiddenStart.value = false
    hiddenEnd.value = false
    return
  }
  const offset = Math.abs(element.scrollLeft)
  hiddenStart.value = offset > 1
  hiddenEnd.value = offset + element.clientWidth < element.scrollWidth - 1
}

/**
 * Re-measures the wrapper. A one-pixel tolerance keeps sub-pixel rounding from turning a table
 * that exactly fits into a phantom Tab stop.
 * @returns Nothing; updates `overflows`.
 */
const measure = (): void => {
  const element = scroller.value
  overflows.value = element !== null && element.scrollWidth > element.clientWidth + 1
  onScroll()
}

onMounted(() => {
  measure()
  observer = new ResizeObserver(measure)
  if (scroller.value !== null) {
    observer.observe(scroller.value)
  }
  if (tableElement.value !== null) {
    observer.observe(tableElement.value)
  }
})

onBeforeUnmount(() => {
  observer?.disconnect()
  observer = null
})
</script>

<template>
  <!-- The outer box only positions the edge fades; the scroller stays the table's direct parent. -->
  <div class="relative w-full max-w-full">
    <div
      ref="scroller"
      class="ui-table-scroller w-full max-w-full overflow-x-auto focus-visible:shadow-focus focus-visible:outline-none"
      :role="overflows ? 'region' : undefined"
      :aria-labelledby="overflows ? captionId : undefined"
      :tabindex="overflows ? 0 : undefined"
      :data-hidden-end="hiddenEnd ? 'true' : undefined"
      data-testid="table-scroller"
      @scroll.passive="onScroll"
    >
      <table
        ref="table"
        class="w-full border-collapse text-left text-sm"
        :aria-busy="loading ? 'true' : undefined"
      >
        <caption :id="captionId" class="sr-only">
          {{
            caption
          }}
        </caption>
        <thead>
          <UiTableRow>
            <UiTableHeaderCell
              v-for="column in columns"
              :key="column.key"
              :align="alignOf(column)"
              :sort="sortOf(column)"
              @sort="requestSort(column)"
            >
              {{ column.label }}
            </UiTableHeaderCell>
          </UiTableRow>
        </thead>
        <tbody v-if="loading">
          <UiTableRow v-for="placeholder in placeholderRows" :key="placeholder" aria-hidden="true">
            <UiTableCell v-for="column in columns" :key="column.key" :align="alignOf(column)" nowrap>
              <UiSkeleton shape="cell" :kind="column.kind" />
            </UiTableCell>
          </UiTableRow>
        </tbody>
        <tbody v-else>
          <UiTableRow v-for="(row, index) in rows" :key="keyOf(row, index)">
            <UiTableCell
              v-for="column in columns"
              :key="column.key"
              :align="alignOf(column)"
              :tone="KIND_TONES[column.kind]"
              :mono="MONO_KINDS.has(column.kind)"
              :strong="STRONG_KINDS.has(column.kind)"
              :nowrap="!WRAP_KINDS.has(column.kind)"
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
.ui-table-scroller :deep(tr > [data-align='end']:last-child) {
  position: sticky;
  right: 0;
  z-index: 1;
  background-color: var(--s1);
}

.ui-table-scroller :deep(tbody tr:hover > [data-align='end']:last-child) {
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
