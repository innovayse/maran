<script setup lang="ts">
/**
 * A column heading inside a {@link UiTable}'s header row.
 *
 * Always renders `<th scope="col">`, never a styled `<td>`: the scope is what lets a screen reader
 * announce "Status: active" when the user lands on a body cell, instead of reading a bare value
 * with no idea which column it came from. Callers pass already-translated text; the primitive holds
 * no copy.
 *
 * The type is the design's small uppercase label: `text-xs`, the display face at weight 800, wide
 * tracking, in the faintest TEXT tier (`text-text-muted`, 5.36:1 on a panel and 5.03:1 on the
 * raised surface in the dark theme; 5.41 / 4.91 in the light one — it passes, so it is not
 * promoted to a brighter tier). There is no header band behind it: the hairline under the header
 * row does the separating.
 *
 * **Headings never wrap.** A two-line heading would push every row of a dense table down, and a
 * long Armenian label such as «ՀԱՅՏԱՐԱՐՈՒԹՅՈՒՆՆԵՐ» is exactly what would otherwise be squeezed into
 * one-word-per-line. The table scrolls inside its own container ({@link UiTable}), so a wide
 * heading widens the table, not the page. Free-standing uppercase labels elsewhere (cards, lists)
 * do wrap anywhere; a table column is the exception because it has somewhere to scroll.
 */
import { computed, type ComputedRef } from 'vue'

/** How the heading sits in its column. */
type CellAlign = 'start' | 'end'

/** Props accepted by {@link UiTableHeaderCell}. */
const props = withDefaults(defineProps<{ align?: CellAlign }>(), { align: 'start' })

/**
 * The alignment class for this heading.
 *
 * A column's heading and its cells must be given the SAME alignment, which is why both primitives
 * take this prop rather than callers reaching for a utility class on one of them.
 */
const alignment: ComputedRef<string> = computed(() => {
  return props.align === 'end' ? 'text-right' : 'text-left'
})
</script>

<template>
  <th
    class="px-3 pt-1 pb-3 font-display text-xs font-extrabold tracking-caps whitespace-nowrap text-text-muted uppercase"
    :class="alignment"
    scope="col"
  >
    <slot />
  </th>
</template>
