<script setup lang="ts">
/**
 * The helper sentence under a form field: what the field accepts, or what leaving it empty means.
 *
 * One primitive, so every helper in the panel is set the same way. Before it, each form typed its
 * own `<p>`, and they drifted: the PHP version picker's helper was `text-xs` while every other one
 * was `text-sm`, so one sentence on the site form read a step smaller than its neighbours (#218).
 *
 * **Step.** `text-sm`, the body step: a helper is a sentence a person reads, and rules/vue.md keeps
 * `text-xs` for micro-labels and badges, never for a phrase. `text-text-muted` is the secondary text
 * tier (4.62:1 or better on every surface). Long words wrap anywhere so an Armenian helper cannot
 * push a 390px screen sideways.
 *
 * The caller supplies the translated text in the default slot; the primitive owns no copy.
 */
</script>

<template>
  <p class="text-sm text-text-muted wrap-anywhere"><slot /></p>
</template>
