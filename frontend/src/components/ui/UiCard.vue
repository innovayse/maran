<script setup lang="ts">
/**
 * Minimal content card: a bordered, padded container used to group related
 * information. First primitive of the shared UI kit (`src/components/ui/`);
 * feature views compose it instead of hand-rolling bordered containers.
 * Takes no props and emits no events — it only lays out its default slot.
 *
 * The design draws every card the same way: the raised surface on a subtle
 * border, a 10px radius, 14px of padding and no shadow — depth comes from the
 * surface ramp, not from a drop shadow. The border strengthens on hover
 * because most cards in the design are clickable; on a static card the change
 * is invisible, since nothing else moves with it.
 */
</script>

<template>
  <div
    class="rounded-xl border border-border-subtle bg-surface-1 p-3.5 transition-colors hover:border-border-strong"
  >
    <slot />
  </div>
</template>
