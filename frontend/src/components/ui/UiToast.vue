<script setup lang="ts">
/**
 * Transient notification: the panel's way of confirming that something
 * happened somewhere other than where the user is looking — a backup finished,
 * a certificate renewed, a save succeeded on a screen that has since scrolled.
 *
 * It is not the place for errors a user must act on. Those belong to
 * {@link UiAlert}, inline and next to the thing that failed, because a message
 * that removes itself after four seconds cannot be re-read and cannot be copied
 * into a support ticket. A toast is for outcomes that need acknowledging, not
 * deciding. Its text is supplied by the caller — already translated chrome, or
 * a message the backend localized (rules/vue.md); the primitive holds no copy.
 */
import { onBeforeUnmount, onMounted, ref, useId, type ComputedRef, type Ref, computed } from 'vue'
import UiIcon from './UiIcon.vue'

/** Tone of a {@link UiToast}, controlling its colour and how urgently it is announced. */
export type ToastVariant = 'success' | 'info' | 'warning' | 'danger'

/** Props accepted by {@link UiToast}. */
const props = withDefaults(
  defineProps<{
    /** Tone of the notification. */
    variant?: ToastVariant
    /**
     * Milliseconds before the toast dismisses itself; `0` keeps it until the
     * user closes it.
     */
    duration?: number
    /** Accessible label for the close button, already translated by the caller. */
    closeLabel: string
  }>(),
  { variant: 'info', duration: 5000 },
)

/** Events emitted by {@link UiToast}. */
const emit = defineEmits<{
  /** The toast asked to be removed — by its timer, by Escape, or by the close button. */
  (event: 'dismiss'): void
}>()

/**
 * Slots exposed by {@link UiToast}.
 * @property default The outcome, in one line of already-translated text.
 * @property meta Optional machine detail beneath it (host, size, duration).
 */
defineSlots<{
  default?: () => unknown
  meta?: () => unknown
}>()

/** Identifier tying the close button's accessible name to this toast's text. */
const messageId: string = useId()

/** Handle of the auto-dismiss timer, or null when the toast waits for the user. */
const timer: Ref<ReturnType<typeof setTimeout> | null> = ref(null)

/**
 * Tailwind utility classes for the selected variant.
 *
 * The design tints only the toast's left edge — the panel itself stays the
 * ordinary raised surface — so the tone reads at a glance without the whole
 * notification turning into a coloured block. Colour never carries the meaning
 * alone: the caller always supplies text, so a user who cannot distinguish the
 * tones still reads what happened.
 */
const variantClasses: ComputedRef<string> = computed(() => {
  switch (props.variant) {
    case 'success':
      return 'border-l-success'
    case 'warning':
      return 'border-l-warning'
    case 'danger':
      return 'border-l-danger'
    default:
      return 'border-l-accent'
  }
})

/**
 * How insistently a screen reader announces this toast.
 *
 * `assertive` interrupts whatever is being read, which is right for a failure
 * and wrong for a confirmation — interrupting someone mid-sentence to say
 * "saved" is worse than saying nothing.
 * @returns The `aria-live` politeness for the current variant.
 */
const politeness: ComputedRef<'assertive' | 'polite'> = computed(() => {
  return props.variant === 'danger' ? 'assertive' : 'polite'
})

/**
 * Stops the auto-dismiss timer, if one is running.
 * @returns Nothing; the timer is cleared synchronously.
 */
const stopTimer = (): void => {
  if (timer.value !== null) {
    clearTimeout(timer.value)
    timer.value = null
  }
}

/**
 * Starts the auto-dismiss timer unless the toast is meant to persist.
 * @returns Nothing; the timer is armed synchronously.
 */
const startTimer = (): void => {
  stopTimer()
  if (props.duration > 0) {
    timer.value = setTimeout((): void => {
      emit('dismiss')
    }, props.duration)
  }
}

/**
 * Reports that the toast should go away.
 * @returns Nothing; the parent decides whether it actually unmounts.
 */
const dismiss = (): void => {
  stopTimer()
  emit('dismiss')
}

// Pointer and keyboard focus both pause the countdown: a toast that vanishes
// while it is being read, or while the user is reaching for its close button,
// is a message the user never received. WCAG 2.2.1 requires the reader to be
// able to extend the time; pausing on hover and focus is how that is met here.
onMounted(startTimer)
onBeforeUnmount(stopTimer)
</script>

<template>
  <div
    class="pointer-events-auto flex max-w-[340px] items-center gap-2.5 rounded-lg border border-border-strong border-l-2 bg-surface-1 px-4 py-3 text-base text-text-primary shadow-[0_12px_32px_rgb(0_0_0/0.35)]"
    :class="variantClasses"
    role="status"
    :aria-live="politeness"
    @mouseenter="stopTimer"
    @mouseleave="startTimer"
    @focusin="stopTimer"
    @focusout="startTimer"
    @keydown.esc="dismiss"
  >
    <div :id="messageId" class="min-w-0 flex-1">
      <div class="font-medium"><slot /></div>
      <!-- The design's second line: the machine detail behind the outcome (host,
           size, duration), set in mono because that is what it always is. It is
           inside the labelled region on purpose, so the announcement carries the
           detail rather than the headline alone. -->
      <div v-if="$slots.meta" class="font-mono text-sm text-text-muted"><slot name="meta" /></div>
    </div>
    <button
      type="button"
      class="inline-flex size-5 shrink-0 items-center justify-center rounded-md text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none"
      :aria-label="closeLabel"
      :aria-describedby="messageId"
      @click="dismiss"
    >
      <!-- An icon, not a text glyph: a character would render differently per font and
           counts as untranslated copy. The accessible name comes from aria-label. -->
      <UiIcon name="x" size="sm" />
    </button>
  </div>
</template>
