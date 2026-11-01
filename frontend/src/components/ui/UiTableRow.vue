<script setup lang="ts">
/**
 * One row inside a {@link UiTable}, in either the header or the body slot. It exists so screens
 * never write `<tr>` themselves (rules/vue.md) while the table keeps the real element structure a
 * screen reader navigates by.
 *
 * Every row carries a 1 px hairline underneath; the table's last body row drops it, since the
 * table's own edge already ends there and a trailing rule would read as an empty row. The rule
 * under the HEADER row is the one that separates the headings from the data, which is why the
 * header row keeps its rule. A body row tints to the raised surface on hover, which is the only
 * cue that a row is the thing the pointer is on; the header row has no hover, because a heading is
 * not a record. Both differences are parent-scoped variants (`tbody` ancestor) so one component
 * serves both slots.
 *
 * The hairline is `border-border-subtle`, about 1.2:1 against the panel: decoration that groups
 * rows, never the only thing that says where a row ends (the cell padding does that too).
 *
 * **As a card** (`card`, the table's phone layout) the row is a bordered `rounded-panel` box laid
 * out as a two-column grid: the title and the actions share the first line, every other cell is a
 * full-width labelled line under them (see {@link UiTableCell}'s `placement`). The hover tint
 * stays; the trailing-rule rule does not apply, since every card has its own whole border.
 */

/** Props accepted by {@link UiTableRow}. */
withDefaults(
  defineProps<{
    /** Draws the row as a card of the table's phone layout. */
    card?: boolean
  }>(),
  { card: false },
)
</script>

<template>
  <tr
    v-if="card"
    class="grid grid-cols-[minmax(0,1fr)_auto] gap-x-3 gap-y-1.5 rounded-panel border border-border-subtle p-3 transition-colors hover:bg-surface-2"
  >
    <slot />
  </tr>
  <tr
    v-else
    class="border-b border-border-subtle transition-colors [tbody_&]:last:border-b-0 [tbody_&]:hover:bg-surface-2"
  >
    <slot />
  </tr>
</template>
