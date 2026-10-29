<script setup lang="ts">
/**
 * Small status label: a pill with a short state inside a dense list (an account's lifecycle state,
 * a locked navigation entry, a task outcome). The badge always renders its text, so colour is
 * never the only carrier of meaning — an accessibility requirement, not a style preference. It
 * owns no copy: callers pass already-translated text (frontend chrome) or backend-localized text
 * through the default slot. Uppercase is applied by CSS rather than by the caller so the underlying
 * text stays translatable and copyable in its own casing.
 *
 * **Why the text is NOT in the status colour.** The obvious badge is green text on a green wash.
 * That pairing is not covered by any token, so it was measured (WCAG relative luminance, wash at
 * 15 % over the surface the badge sits on). It fails, in both themes, on surfaces a badge really
 * lands on — a hovered table row is the raised surface:
 *
 *   dark,  status text on its own wash, surface 1 / 2 / 3:
 *     success 5.73 / 5.30 / 4.82   warning 6.46 / 5.97 / 5.42   danger 4.15 / 3.87 / 3.55
 *     accent (as text) 5.16 / 4.80 / 4.39   violet 4.29 / 3.99 / 3.64
 *   light, same pairs (accent wash at 10 %, violet at 9 %):
 *     success 4.40 / 4.03 / 3.77   warning 4.60 / 4.21 / 3.94   danger 4.44 / 4.05 / 3.79
 *     accent 6.03 / 5.50 / 5.14   violet 5.13 / 4.67 / 4.36
 *
 * Danger and violet fail on every dark surface, success and warning fail on the light raised
 * surface, and the light danger fails on the hovered row. No colour may be invented to fix that, so
 * the status is carried by two other things and the text stays primary: the wash is tinted with the
 * status colour, and a small dot in the status colour leads the text. The text is `text-text-primary`
 * on the wash — 10.3 to 13.6:1 in dark and 12.1 to 15.9:1 in light, on all three surfaces. The dot
 * is a graphic that only repeats what the words say, yet it is still held to the 3:1 graphics bar:
 * the dot against its own wash is 3.55 to 6.46:1 in dark and 3.77 to 6.03:1 in light, on all three
 * surfaces (the accent dot uses the accent TEXT token, because the lit core is 2.94:1 on the light
 * wash over surface 3). Re-measure if a status token or the wash alpha moves.
 *
 * The neutral badge is `text-text-secondary` on the third surface step: 6.11:1 dark, 5.06:1 light.
 * It is a pill against a panel, which is decoration at about 1.1:1 — the word is the content.
 *
 * **A badge holds one line in a table, and never outgrows its container anywhere else.** It
 * normally holds one short state word, so a break inside it ("SUCCEE / DED", a letter per line in a
 * long Armenian word) can only be the result of a squeezed container, and a half-word pill reads as
 * a different, broken state. `shrink-0` stops a flex row from compressing the pill below its
 * content, and in a table cell (`[td_&]` / `[th_&]`) `whitespace-nowrap` stops the break: the table
 * scrolls inside its own wrapper (see UiTable) rather than squeeze its pills. There is deliberately
 * no `overflow-wrap: anywhere` here: that value lowers the pill's minimum content width to one
 * character, which is exactly what lets a table squash it.
 *
 * Outside a table the pill is `max-w-full`, and its text may wrap — at WORD boundaries only, because
 * no `overflow-wrap`/`word-break` is set. A status word that fits (every one of them) never wraps;
 * only a long sentence-like badge ("INSPECTION — NOTHING CHANGED", its Armenian equivalent) that
 * would otherwise push 4 to 8 px past its card at 320 px breaks between words instead. The one-line
 * rule is kept for tables because a table cell's width comes from its content's minimum width: a
 * wrappable badge there would let the column collapse to the longest word.
 */
import { computed, type ComputedRef } from 'vue'

/**
 * Severity/tone of a {@link UiBadge}. Declared here rather than in `src/types/` because it
 * describes this component's props and means nothing without it (rules/vue.md); exported because
 * feature components map their own domain state onto it — an account status, a task outcome.
 */
export type BadgeVariant = 'neutral' | 'success' | 'warning' | 'danger' | 'info' | 'ai'

/** Props accepted by {@link UiBadge}. */
const props = withDefaults(
  defineProps<{
    /** Tone of the badge, controlling its wash and the dot that leads its text. */
    variant?: BadgeVariant
  }>(),
  { variant: 'neutral' },
)

/** The pill's background and text colour for the selected {@link BadgeVariant}. */
const pillClasses: ComputedRef<string> = computed(() => {
  switch (props.variant) {
    case 'success':
      return 'bg-success/15 text-text-primary'
    case 'warning':
      return 'bg-warning/15 text-text-primary'
    case 'danger':
      return 'bg-danger/15 text-text-primary'
    case 'info':
      return 'bg-accent-soft text-text-primary'
    case 'ai':
      return 'bg-violet-soft text-text-primary'
    case 'neutral':
    default:
      return 'bg-surface-3 text-text-secondary'
  }
})

/**
 * The leading dot's colour, or `null` for the neutral badge, which has no status to name and so
 * draws no dot (a grey dot would be a status of its own).
 */
const dotClasses: ComputedRef<string | null> = computed(() => {
  switch (props.variant) {
    case 'success':
      return 'bg-success'
    case 'warning':
      return 'bg-warning'
    case 'danger':
      return 'bg-danger'
    case 'info':
      return 'bg-accent-text'
    case 'ai':
      return 'bg-violet'
    case 'neutral':
    default:
      return null
  }
})
</script>

<template>
  <span
    class="inline-flex max-w-full shrink-0 items-center gap-1.5 rounded-full px-2.5 py-1 font-display text-xs leading-none font-extrabold tracking-caps uppercase [td_&]:whitespace-nowrap [th_&]:whitespace-nowrap"
    :class="pillClasses"
  >
    <!-- Decorative: the word beside it already says the state. -->
    <span v-if="dotClasses !== null" class="size-1.5 shrink-0 rounded-full" :class="dotClasses" aria-hidden="true" />
    <slot />
  </span>
</template>
