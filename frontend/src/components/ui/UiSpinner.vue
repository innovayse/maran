<script setup lang="ts">
/**
 * Indeterminate loading indicator. Announced to assistive tech via
 * `role="status"` and a caller-supplied, already-translated label — the
 * spinner itself owns no text, so it carries no i18n key of its own.
 */

/** Props accepted by {@link UiSpinner}. */
defineProps<{
  /** Accessible label describing what is loading (translated by the caller). */
  label: string
}>()
</script>

<template>
  <span role="status" class="inline-flex items-center gap-2 text-sm text-slate-600">
    <span
      class="h-4 w-4 animate-spin rounded-full border-2 border-slate-300 border-t-blue-600"
      aria-hidden="true"
    />
    <span class="sr-only">{{ label }}</span>
  </span>
</template>
