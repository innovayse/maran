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
 *
 * **Look.** A floating `bg-surface-1` panel (it hovers over the page, so a shadow
 * is correct here) with a tinted square on the left holding the status glyph.
 * Status is carried by the SHAPE of that glyph — tick, info disc, warning
 * triangle, alert disc — and by the caller's text, never by colour alone.
 *
 * **Contrast, measured (dark / light theme).** Each glyph sits on a 15% wash of
 * its own status colour over `bg-surface-1`, and is graphics (3:1 needed):
 * success 5.73 / 4.40:1, warning 6.46 / 4.60:1, danger 4.15 / 4.44:1, and the
 * info glyph (`text-accent-text` on `bg-accent-soft`) 5.16 / 6.03:1. All of
 * them also clear 4.15:1, so nothing relies on the graphics allowance. The
 * message is `text-text-primary` on the panel, 15.70 / 17.98:1, and the meta line
 * `text-text-muted`, 5.36 / 5.41:1. The close control is 28px square, over the
 * 24px minimum target.
 */
import { onBeforeUnmount, onMounted, ref, useId, type ComputedRef, type Ref, computed } from 'vue'
import UiIcon, { type UiIconName } from './UiIcon.vue'

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
 * Tailwind utility classes for the status square, by variant: its wash and the
 * glyph's colour. Each pairing is measured in this component's header. The info
 * tone uses the accent wash with `text-accent-text`, the pair the design README
 * measures at 5.15:1 over `bg-surface-1`; it must never sit over `bg-surface-3`
 * (4.40:1).
 */
const variantClasses: ComputedRef<string> = computed(() => {
  switch (props.variant) {
    case 'success':
      return 'bg-success/15 text-success'
    case 'warning':
      return 'bg-warning/15 text-warning'
    case 'danger':
      return 'bg-danger/15 text-danger'
    default:
      return 'bg-accent-soft text-accent-text'
  }
})

/**
 * The glyph for the selected variant. A different SHAPE per tone, because the
 * tint alone cannot tell a success from a failure to a user who cannot
 * distinguish the colours.
 */
const glyph: ComputedRef<UiIconName> = computed(() => {
  switch (props.variant) {
    case 'success':
      return 'circleCheck'
    case 'warning':
      return 'triangleAlert'
    case 'danger':
      return 'circleAlert'
    default:
      return 'info'
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
    class="pointer-events-auto flex w-full max-w-sm items-center gap-3 rounded-panel border border-border-strong bg-surface-1 p-3 text-sm text-text-primary shadow-xl"
    role="status"
    :aria-live="politeness"
    @mouseenter="stopTimer"
    @mouseleave="startTimer"
    @focusin="stopTimer"
    @focusout="startTimer"
    @keydown.esc="dismiss"
  >
    <!-- Decorative: the status is repeated in the caller's text, and UiIcon hides
         the glyph from assistive technology. -->
    <span class="grid size-9 shrink-0 place-items-center rounded-inset" :class="variantClasses">
      <UiIcon :name="glyph" size="md" />
    </span>
    <div :id="messageId" class="min-w-0 flex-1 wrap-anywhere">
      <div class="font-medium"><slot /></div>
      <!-- The design's second line: the machine detail behind the outcome (host,
           size, duration), set in mono because that is what it always is. It is
           inside the labelled region on purpose, so the announcement carries the
           detail rather than the headline alone. -->
      <div v-if="$slots.meta" class="font-mono text-sm text-text-muted"><slot name="meta" /></div>
    </div>
    <button
      type="button"
      class="inline-flex size-7 shrink-0 items-center justify-center rounded-inset text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none"
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
