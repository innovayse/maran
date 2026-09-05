<script setup lang="ts">
/**
 * A column heading inside a {@link UiTable}'s header row.
 *
 * Always renders `<th scope="col">`, never a styled `<td>`: the scope is what
 * lets a screen reader announce "Status: active" when the user lands on a body
 * cell, instead of reading a bare value with no idea which column it came from.
 * Callers pass already-translated text; the primitive holds no copy.
 *
 * The type is the design's `th` helper verbatim — 10.5px, 600 weight, .06em
 * tracking, uppercase, muted, 8px/12px padding — and headings never wrap,
 * because a two-line heading would push every row of a dense table down.
 */

/** How the heading sits in its column. */
type CellAlign = 'start' | 'end'

const props = withDefaults(defineProps<{ align?: CellAlign }>(), { align: 'start' })

/**
 * The alignment class for this heading.
 *
 * A column's heading and its cells must be given the SAME alignment, which is
 * why both primitives take this prop rather than callers reaching for a utility
 * class on one of them. A right-aligned row action under a left-aligned
 * "Actions" heading is what this exists to prevent: the heading then names a
 * column it does not sit above, and on a wide table the two are inches apart.
 * @returns The Tailwind text-alignment class for this column.
 */
const alignment = (): string => {
  return props.align === 'end' ? 'text-right' : 'text-left'
}
</script>

<template>
  <th
    class="px-4 py-2.5 text-sm font-semibold tracking-caps whitespace-nowrap text-text-muted uppercase"
    :class="alignment()"
    scope="col"
  >
    <slot />
  </th>
</template>
