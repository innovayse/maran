<script setup lang="ts">
/**
 * Navigation region: a labelled `<nav>` wrapping a vertical list of
 * {@link UiNavItem} entries. Exists so feature code never writes the
 * `nav`/`ul` markup itself (rules/vue.md) while the semantics a screen reader
 * depends on — a navigation landmark containing a real list — stay intact.
 *
 * The label is required, not optional: a page may hold several navigation
 * landmarks (sidebar, breadcrumbs, the phone tab bar) and an unlabelled one
 * leaves a screen-reader user with identical "navigation" entries and no way to
 * tell them apart. Callers pass already-translated text; the primitive holds no
 * copy.
 */

/** Props accepted by {@link UiNav}. */
defineProps<{
  /** Accessible name of this navigation landmark, already translated. */
  label: string
}>()
</script>

<template>
  <!-- No width and no padding: a kit primitive that bakes in a layout forces its
       second consumer to fight it, so the caller sizes the region. The 2px gap is
       the dense step (issue #200): enough that two 12px-radius pills never touch,
       without spending more of a short screen on air. -->
  <nav class="w-full" :aria-label="label">
    <ul class="flex flex-col gap-0.5">
      <slot />
    </ul>
  </nav>
</template>
