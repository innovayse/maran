<script setup lang="ts">
/**
 * A label/value list for the detail screens — the "here is what this thing is"
 * block under a heading, holding {@link UiDescriptionItem} children.
 *
 * Renders a real `<dl>`, which is what makes the pairing readable to assistive
 * technology: a screen reader announces "Domain, example.com" as one unit,
 * where the same thing built from `<div>`s is two unrelated strings. Feature
 * code composed the `<dl>` by hand on three screens before this existed, and
 * the grid was retyped each time (rules/vue.md: the kit owns the markup).
 *
 * One column on a narrow screen, two from the `sm` breakpoint up: a label and
 * its value must sit together, and side-by-side pairs on a phone leave neither
 * half enough width to avoid wrapping mid-value.
 *
 * Takes no props — a list that needed to vary would be a different primitive,
 * not a flag on this one.
 */
</script>

<template>
  <dl class="grid gap-3 sm:grid-cols-2">
    <slot />
  </dl>
</template>
