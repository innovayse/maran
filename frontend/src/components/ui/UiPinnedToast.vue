<script setup lang="ts">
/**
 * A screen's outcome notice, pinned to the bottom corner of the viewport over the page: a
 * {@link UiToast} in the one place every screen shows it.
 *
 * One component so every create in the panel confirms itself the same way, in the same place
 * (#222): the screens used to repeat the pinned wrapper by hand, and the ones that did not simply
 * said nothing. Nothing is drawn while `message` is `null`; the screen sets the already-translated
 * text when its store reports the outcome and clears it on `dismiss`. A confirmation is `success`,
 * the default; a screen whose outcome can be partial passes `warning`.
 *
 * The wrapper lets clicks through (`pointer-events-none`) so the page under it stays usable; only
 * the toast itself takes the pointer.
 */
import UiToast, { type ToastVariant } from './UiToast.vue'

/** Props accepted by {@link UiPinnedToast}. */
withDefaults(
  defineProps<{
    /** The notice, already translated; `null` while there is nothing to say. */
    message: string | null
    /** Accessible label for the close button, already translated by the caller. */
    closeLabel: string
    /** Tone of the notice. */
    variant?: ToastVariant
  }>(),
  { variant: 'success' },
)

/** Events emitted by {@link UiPinnedToast}. */
const emit = defineEmits<{
  /** The toast left the screen — by its timer, by Escape, or by the close button. */
  (event: 'dismiss'): void
}>()
</script>

<template>
  <div class="pointer-events-none fixed inset-x-4 bottom-4 z-50 flex justify-end sm:left-auto">
    <UiToast v-if="message !== null" :variant="variant" :close-label="closeLabel" @dismiss="emit('dismiss')">
      {{ message }}
    </UiToast>
  </div>
</template>
