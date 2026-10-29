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
 */
</script>

<template>
  <tr
    class="border-b border-border-subtle transition-colors motion-reduce:transition-none [tbody_&]:last:border-b-0 [tbody_&]:hover:bg-surface-2"
  >
    <slot />
  </tr>
</template>
