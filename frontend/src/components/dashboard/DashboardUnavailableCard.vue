<script setup lang="ts">
/**
 * A landing-page section the panel could not read: its heading, and one sentence saying so.
 *
 * Drawn in the section's own place rather than leaving a gap, because a missing card and a card that
 * was never meant to be there look the same to a reader, and "could not be read" is the one honest
 * thing the screen can say about it (issue #248). The sentence is the SPA's own chrome — the panel
 * reports only that the section is `unavailable`, and why is in the server's log.
 */
import UiCard from '../ui/UiCard.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'

defineProps<{
  /** The section's heading, as it reads when the section is drawn. */
  title: string
  /** The sentence saying what could not be read. */
  message: string
}>()
</script>

<template>
  <section>
    <UiSectionHeading class="mb-3" :title="title" />
    <UiCard>
      <p class="m-0 text-sm text-text-secondary">{{ message }}</p>
    </UiCard>
  </section>
</template>
