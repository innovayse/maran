<script setup lang="ts">
/**
 * Inline notice: a status or error banner. Used to render server-provided text
 * verbatim (rules/vue.md: "the backend owns their text") as well as
 * frontend-owned chrome messages — the primitive holds no copy of its own, so
 * it adds no "Error:" prefix either; the caller's sentence is the whole text.
 *
 * Four meanings, each carried by an ICON as well as by colour, so a reader who
 * cannot tell the hues apart still gets the severity from the glyph's shape:
 * `info` (a circled i, the accent), `success` (a ticked circle, green),
 * `warning` (a triangle, AMBER) and `error` (an exclamation in a circle, red).
 * `danger` is accepted as another spelling of `error`, because the badge uses
 * that word and a caller moving between the two should not have to remember
 * which component says which.
 *
 * A warning is amber and never blue. With a blue brand a blue notice reads as
 * "information"; an account running out of room has to read as a warning, and
 * the amber token already carries that meaning.
 *
 * The message itself is set in the primary text colour on every variant, and
 * only the icon and the border take the status colour. That is a contrast
 * decision, not a taste one: status text on a wash of its own colour is the
 * pairing no token covers, and it fails — the danger icon colour is 4.39:1 on
 * its own wash over a panel (4.10:1 over a raised surface), under the 4.5:1
 * text needs. Measured with the WCAG relative-luminance formula, the washes
 * being the status colour at 10% (info: the accent wash, 15% dark / 10% light)
 * composited over the page, the panel (`--s1`) and the raised surface (`--s2`):
 *
 * - Message, primary text: 12.42 to 15.62:1 in the dark theme and 14.01 to
 *   15.94:1 in the light one, across all four variants and all three surfaces.
 * - Icon, as a graphic (3:1 needed): 4.10 to 7.82:1 dark, 4.31 to 6.03:1 light.
 *
 * Both pass with room. Re-measure if a status token or the wash alpha moves.
 *
 * Politeness: `error` is announced assertively so a screen reader interrupts
 * for it; every other variant is polite. The role stays `status` on all of
 * them — the end-to-end suite finds the banner by it, and an assertive live
 * region does the interrupting without needing `role="alert"`.
 *
 * Where the live region sits: on the element that directly holds the message,
 * not on the notice's outer box. The outer box also holds the icon and the
 * `actions` slot; a live region around them would announce a button's label
 * with the message every time the notice changed, and the end-to-end suite
 * finds a notice by its text and then reads `aria-live` off that same element
 * (`e2e/sites/logs-tab.spec.ts`), which only holds when the two coincide.
 *
 * Layout: the text is the flexible part and wraps anywhere (a server message
 * can carry a long path or an unbroken token). The optional `actions` slot
 * holds a button or link that belongs to the notice; it sits beside the text
 * when there is room and WRAPS BELOW it when there is not, because a notice
 * with a button squeezed beside it collapsed to one word per line at 390px
 * while the design was being built.
 */
import { computed, type ComputedRef } from 'vue'
import UiIcon, { type UiIconName } from './UiIcon.vue'

/**
 * Severity of a {@link UiAlert}. Declared here because it describes this
 * component's props and means nothing without it (rules/vue.md). `error` is the
 * original spelling the pages use; `danger` is an alias of it.
 */
export type UiAlertVariant = 'info' | 'success' | 'warning' | 'error' | 'danger'

/** Props accepted by {@link UiAlert}. */
const props = withDefaults(
  defineProps<{
    /** Severity, controlling the icon, the colour and the ARIA live-region politeness. */
    variant?: UiAlertVariant
  }>(),
  { variant: 'info' },
)

/**
 * Slots exposed by {@link UiAlert}.
 * @property default The notice's message, already translated or sent by the server.
 * @property actions Optional button or link that belongs to the notice; wraps below the message when the row is narrow.
 */
defineSlots<{
  default?: () => unknown
  actions?: () => unknown
}>()

/** Whether the variant is the destructive one, under either of its spellings. */
const isError: ComputedRef<boolean> = computed(() => {
  return props.variant === 'error' || props.variant === 'danger'
})

/**
 * The glyph that names the severity by shape. Each is a different silhouette
 * — circle, tick, triangle, bang — so the meaning survives without hue.
 */
const iconName: ComputedRef<UiIconName> = computed(() => {
  if (isError.value) {
    return 'circleAlert'
  }
  switch (props.variant) {
    case 'success':
      return 'circleCheck'
    case 'warning':
      return 'triangleAlert'
    case 'info':
    default:
      return 'info'
  }
})

/**
 * Border and wash classes for the severity. The wash is the status colour at
 * 10% over whatever surface the notice sits on (the info wash is the accent
 * token's own), and the border the same colour at 40%: strong enough to read
 * as an edge, never the only cue (the icon and the text carry the meaning).
 */
const toneClasses: ComputedRef<string> = computed(() => {
  if (isError.value) {
    return 'border-danger/40 bg-danger/10'
  }
  switch (props.variant) {
    case 'success':
      return 'border-success/40 bg-success/10'
    case 'warning':
      return 'border-warning/40 bg-warning/10'
    case 'info':
    default:
      return 'border-accent/40 bg-accent-soft'
  }
})

/**
 * The colour of the icon alone. Info uses the accent TEXT step (`--act`), not
 * the graphics core, because it is blue on a dark panel and has to hold up in
 * the light theme too, where `--act` is the darker step.
 */
const iconClasses: ComputedRef<string> = computed(() => {
  if (isError.value) {
    return 'text-danger'
  }
  switch (props.variant) {
    case 'success':
      return 'text-success'
    case 'warning':
      return 'text-warning'
    case 'info':
    default:
      return 'text-accent-text'
  }
})
</script>

<template>
  <div
    class="flex flex-wrap items-start gap-x-3 gap-y-3 rounded-panel border px-4 py-3 text-sm leading-normal text-text-primary"
    :class="toneClasses"
  >
    <div class="flex min-w-0 flex-1 basis-60 items-start gap-3">
      <!-- Decorative: UiIcon hides itself from assistive technology, and the
           message beside it already says everything the glyph does. -->
      <span class="mt-px shrink-0" :class="iconClasses"><UiIcon :name="iconName" /></span>
      <!-- The live region is the element that holds the message itself, not the
           whole notice: assertive for errors so screen readers interrupt, polite
           otherwise. See "Where the live region sits" in the doc comment. -->
      <div class="min-w-0 flex-1 wrap-anywhere" role="status" :aria-live="isError ? 'assertive' : 'polite'">
        <slot />
      </div>
    </div>
    <div v-if="$slots.actions" class="flex flex-wrap items-center gap-2">
      <slot name="actions" />
    </div>
  </div>
</template>
