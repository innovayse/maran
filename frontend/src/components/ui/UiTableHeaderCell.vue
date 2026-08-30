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
</script>

<template>
  <th
    class="px-3 py-2 text-2xs font-semibold tracking-caps whitespace-nowrap text-text-muted uppercase"
    scope="col"
  >
    <slot />
  </th>
</template>
