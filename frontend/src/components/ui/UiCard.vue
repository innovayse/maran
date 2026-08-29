<script setup lang="ts">
/**
 * Minimal content card: a bordered, padded container used to group related
 * information. First primitive of the shared UI kit (`src/components/ui/`);
 * feature views compose it instead of hand-rolling bordered containers.
 * Takes no props and emits no events — it only lays out its default slot.
 */
</script>

<template>
  <!-- Design-token colors only (rules/vue.md: no hardcoded hex values). -->
  <div class="rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
    <slot />
  </div>
</template>
