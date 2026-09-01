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
</script>

<template>
  <td class="px-4 py-3.5 align-middle text-text-primary">
    <slot />
  </td>
</template>
