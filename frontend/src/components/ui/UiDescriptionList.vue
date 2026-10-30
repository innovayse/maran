<script setup lang="ts">
/**
 * A label/value list for the detail screens — the "here is what this thing is" block under a
 * heading, holding {@link UiDescriptionItem} children.
 *
 * Renders a real `<dl>`, which is what makes the pairing readable to assistive technology: a screen
 * reader announces "Domain, example.com" as one unit, where the same thing built from `<div>`s is
 * two unrelated strings. Feature code composed the `<dl>` by hand on three screens before this
 * existed, and the grid was retyped each time (rules/vue.md: the kit owns the markup).
 *
 * One column on a narrow screen, two from the `sm` breakpoint up: a label and its value must sit
 * together, and side-by-side pairs on a phone leave neither half enough width to avoid wrapping
 * mid-value. `min-w-0` on the children (see {@link UiDescriptionItem}) is what keeps a long value —
 * a path, a hash — inside its column instead of widening the page at 390 px.
 *
 * Its one prop is `loading`, which is not a variant of the layout: while the record is still
 * loading, every {@link UiDescriptionItem} keeps its real term and draws a value line in place of
 * the value, so the list's own template is its skeleton and the loaded values land in the same
 * space. The list is `aria-busy` meanwhile; the announcement belongs to the {@link UiLoadingRegion}
 * around it.
 */
import { provide, toRef } from 'vue'
import { DESCRIPTION_LIST_LOADING_KEY } from '../../utils/descriptionListLoadingKey'

/** Props accepted by {@link UiDescriptionList}. */
const props = withDefaults(
  defineProps<{
    /** Whether the record is still loading; each item draws a value line meanwhile. */
    loading?: boolean
  }>(),
  { loading: false },
)

provide(DESCRIPTION_LIST_LOADING_KEY, toRef(props, 'loading'))
</script>

<template>
  <dl class="grid gap-x-5 gap-y-3 sm:grid-cols-2" :aria-busy="loading ? 'true' : undefined">
    <slot />
  </dl>
</template>
