<script setup lang="ts">
/**
 * Placeholder content for a screen with nothing to show — an unknown route,
 * an empty list, a not-yet-populated panel. Renders a heading, an optional
 * description, and a slot for actions (e.g. a `UiNavLink` back home).
 */

/** Props accepted by {@link UiEmptyState}. */
defineProps<{
  /** Short heading naming the empty condition (already translated by the caller). */
  title: string
  /** Optional longer explanation (already translated by the caller). */
  description?: string
}>()
</script>

<template>
  <div class="flex flex-col items-center gap-2 rounded-lg border border-dashed border-slate-300 p-8 text-center">
    <p class="text-base font-semibold text-slate-900">{{ title }}</p>
    <p v-if="description" class="text-sm text-slate-600">{{ description }}</p>
    <div v-if="$slots.default" class="mt-2">
      <slot />
    </div>
  </div>
</template>
