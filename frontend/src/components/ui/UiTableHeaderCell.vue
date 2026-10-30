<script setup lang="ts">
/**
 * A column heading inside a {@link UiTable}'s header row.
 *
 * Always renders `<th scope="col">`, never a styled `<td>`: the scope is what lets a screen reader
 * announce "Status: active" when the user lands on a body cell, instead of reading a bare value
 * with no idea which column it came from. Callers pass already-translated text; the primitive holds
 * no copy.
 *
 * The type is the design's small label in sentence case: `text-xs`, the display face at weight 800,
 * in the faintest TEXT tier (`text-text-muted`, 5.36:1 on a panel and 5.03:1 on the
 * raised surface in the dark theme; 5.41 / 4.91 in the light one — it passes, so it is not
 * promoted to a brighter tier). There is no header band behind it: the hairline under the header
 * row does the separating.
 *
 * **Headings never wrap.** A two-line heading would push every row of a dense table down, and a
 * long Armenian label such as «ՀԱՅՏԱՐԱՐՈՒԹՅՈՒՆՆԵՐ» is exactly what would otherwise be squeezed into
 * one-word-per-line. The table scrolls inside its own container ({@link UiTable}), so a wide
 * heading widens the table, not the page. Free-standing micro-labels elsewhere (cards, lists)
 * do wrap anywhere; a table column is the exception because it has somewhere to scroll.
 *
 * `data-align` lets {@link UiTable} keep an end-aligned last heading sticky over the sticky action
 * column beneath it.
 *
 * **Sortable columns.** Given `sort`, the heading's text becomes a button that emits `sort`, and the
 * `th` carries `aria-sort`, so a screen reader announces the column's order on the heading itself
 * rather than through an icon. The glyph beside the text is decoration: up for ascending, down for
 * descending, a pair of chevrons for a column that can be sorted but is not. The button's name is
 * the heading's text, the same name a reader hears for the column. Without `sort` the heading is
 * plain text, as before. The caller owns the order: the component never sorts anything itself.
 */
import { computed, type ComputedRef } from 'vue'
import UiIcon, { type UiIconName } from './UiIcon.vue'

/** How the heading sits in its column. */
type CellAlign = 'start' | 'end'

/** The order a sortable column is in: by it ascending, by it descending, or not by it. */
export type ColumnSort = 'ascending' | 'descending' | 'none'

/** Props accepted by {@link UiTableHeaderCell}. */
const props = withDefaults(
  defineProps<{
    /** How the heading sits in its column. */
    align?: CellAlign
    /** The column's current order; left out for a column that cannot be sorted. */
    sort?: ColumnSort
  }>(),
  { align: 'start', sort: undefined },
)

/** Events emitted by {@link UiTableHeaderCell}. */
const emit = defineEmits<{
  /** The reader asked to sort by this column; only a sortable heading emits it. */
  (e: 'sort'): void
}>()

/** The glyph beside a sortable heading's text. */
const sortIcon: ComputedRef<UiIconName> = computed(() => {
  if (props.sort === 'ascending') {
    return 'chevronUp'
  }
  return props.sort === 'descending' ? 'chevronDown' : 'chevronsUpDown'
})

/**
 * The alignment class for this heading.
 *
 * A column's heading and its cells must be given the SAME alignment, which is why both primitives
 * take this prop rather than callers reaching for a utility class on one of them.
 */
const alignment: ComputedRef<string> = computed(() => {
  return props.align === 'end' ? 'text-right' : 'text-left'
})

/**
 * Reports a press on a sortable heading.
 * @returns Nothing.
 */
const requestSort = (): void => {
  emit('sort')
}
</script>

<template>
  <th
    class="px-3 pt-0.5 pb-1.5 font-display text-xs font-extrabold whitespace-nowrap text-text-muted"
    :class="alignment"
    :data-align="align"
    :aria-sort="sort"
    scope="col"
  >
    <button
      v-if="sort !== undefined"
      type="button"
      class="inline-flex items-center gap-1 rounded-inset font-display text-xs font-extrabold hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none"
      :class="sort === 'none' ? '' : 'text-text-primary'"
      data-testid="sort-header"
      @click="requestSort"
    >
      <slot />
      <UiIcon :name="sortIcon" size="sm" />
    </button>
    <slot v-else />
  </th>
</template>
