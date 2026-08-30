<script setup lang="ts">
/**
 * Navigation region: a labelled `<nav>` wrapping a vertical list of
 * {@link UiNavItem} entries. Exists so feature code never writes the
 * `nav`/`ul` markup itself (rules/vue.md) while the semantics a screen reader
 * depends on — a navigation landmark containing a real list — stay intact.
 *
 * The label is required, not optional: a page may hold several navigation
 * landmarks (sidebar, breadcrumbs, pagination) and an unlabelled one leaves a
 * screen-reader user with two identical "navigation" entries and no way to tell
 * them apart. Callers pass already-translated text; the primitive holds no copy.
 */

/** Props accepted by {@link UiNav}. */
defineProps<{
  /** Accessible name of this navigation landmark, already translated. */
  label: string
}>()
</script>

<template>
  <!-- The design's sidebar packs its entries a single pixel apart: the group
       label above them does the separating, so a larger gap would only make the
       list look unfinished. -->
  <!-- No width and no padding: a kit primitive that bakes in a layout forces
       its second consumer to fight it. This one was 224px wide inside a 246px
       sidebar whose scroller already had its own inset, so the rows neither
       filled the column nor lined up with the design. The caller sizes it. -->
  <nav class="w-full" :aria-label="label">
    <ul class="flex flex-col gap-px">
      <slot />
    </ul>
  </nav>
</template>
