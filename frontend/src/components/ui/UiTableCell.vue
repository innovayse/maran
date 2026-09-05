<script setup lang="ts">
/**
 * One data cell inside a {@link UiTable} body row. Carries the table's cell
 * padding so every screen's rows line up without each one restating the
 * spacing (rules/vue.md: no raw elements, no ad-hoc utility classes in
 * feature code).
 *
 * 11px/12px is the design's row padding: taller than the header band so a row
 * of mixed content (a name over an address, a badge, a bar) has room to sit on
 * one baseline.
 */

/** How the cell's content sits in its column. */
type CellAlign = 'start' | 'end'

const props = withDefaults(defineProps<{ align?: CellAlign }>(), { align: 'start' })

/**
 * The alignment class for this cell.
 *
 * Pass the same value to this column's {@link UiTableHeaderCell}. Feature code
 * used to right-align a row-action trigger with a flex wrapper inside the cell
 * and leave the heading alone, so the heading sat at the far left of a column
 * whose only content sat at the far right — visibly broken, and unfixable from
 * the heading because it had no way to say where its column's content lives.
 * @returns The Tailwind text-alignment class for this column.
 */
const alignment = (): string => {
  return props.align === 'end' ? 'text-right' : 'text-left'
}
</script>

<template>
  <td class="px-4 py-3.5 align-middle text-text-primary" :class="alignment()">
    <slot />
  </td>
</template>
