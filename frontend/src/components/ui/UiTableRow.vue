<script setup lang="ts">
/**
 * One row inside a {@link UiTable}, in either the header or the body slot.
 * Exists so screens never write `<tr>` themselves (rules/vue.md) while the
 * table keeps the real element structure a screen reader navigates by.
 *
 * The design separates body rows with a top border and lifts the row to the
 * raised surface on hover; the header row does neither, since its own fill
 * already divides it from the body. Both are expressed as parent-scoped
 * variants so the one component keeps serving both slots.
 */
</script>

<template>
  <tr
    class="border-t border-border-subtle transition-colors [thead_&]:border-t-0 [tbody_&]:hover:bg-surface-2"
  >
    <slot />
  </tr>
</template>
