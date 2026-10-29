<script setup lang="ts">
/**
 * The panel's only button primitive. Renders a real `<button>` so focus,
 * keyboard activation and `disabled` semantics come for free; every other
 * screen composes this instead of a raw `<button>` (rules/vue.md: "UI comes
 * from components/ui").
 *
 * **Shape.** The primary action is the one full pill (`rounded-full`) with the
 * accent glow under it; every other variant is a `rounded-control` rectangle.
 * One pill per screen is what lets the eye find "the" action.
 *
 * **Contrast, measured (WCAG relative luminance, dark / light theme).**
 *
 * - `primary`: white on `bg-accent-fill` is 5.14:1 and white on the hover
 *   `bg-accent-hover` is 6.80:1 in both themes. White must NEVER sit on
 *   `bg-accent` (`#2E7BFF`, 3.89:1): 14px semibold text needs 4.5:1, so that
 *   pairing is a defect, not a style choice.
 * - `secondary`: primary text on `bg-surface-2` is 14.75 / 16.31:1, and 13.55 /
 *   15.17:1 on the `bg-surface-3` hover.
 * - `ghost`: secondary text is 7.08 / 6.00:1 on a panel and 6.11 / 5.06:1 on the
 *   `bg-surface-3` hover, where the label also steps up to primary text.
 * - `quiet`: a ghost one step dimmer, for a control that must not compete with its panel (a
 *   sidebar's collapse toggle). `text-text-muted` is 5.36 / 5.41:1 on a panel and 4.62 / 4.57:1 on the
 *   `bg-surface-3` hover, where the icon still steps up to primary text as the ghost's does.
 * - `field`: a button drawn as the input it stands in for (the app bar's search field, which opens
 *   the command palette). `text-text-muted` on `bg-surface-2` is 5.03 / 4.91:1, the border is the
 *   control border (`border-border-control`, 3:1 as a graphic) and the content starts at the left
 *   edge like a placeholder does.
 * - `destructive`: `text-danger` is 4.79 / 5.62:1 on its `bg-surface-1` fill and
 *   5.12 / 5.24:1 on the `bg-page` hover. It is deliberately NOT a red wash: a
 *   wash lifts the background and drops `text-danger` under 4.5:1, and `text-danger`
 *   on `bg-surface-3` already fails (4.13:1). The red border carries the warning
 *   (4.79:1 against the panel as a graphic), the label is what a user reads.
 * - `ai`: `text-violet` is 4.95 / 5.83:1 on `bg-surface-1`, 5.29 / 5.44:1 on the
 *   `bg-page` hover. There is no violet fill token that carries white text, so
 *   this variant is outlined for the same reason `destructive` is.
 * - disabled: `text-text-muted` on `bg-surface-2` is 5.03 / 4.91:1 — a disabled
 *   control is exempt from contrast, but it stays readable.
 *
 * **Accessible name.** The slot is the name. A button whose only content is an
 * icon MUST pass its own translated `aria-label` (it falls through to the
 * `<button>`): an icon is `aria-hidden` by default, so without a label the
 * control has no name at all. Every size is at least 32px on each side (`icon-sm`; 40px
 * for the rest), so the 24x24px minimum target (WCAG 2.5.8) is cleared with room to spare.
 *
 * **Size.** `size` is the geometry and `variant` the look; they are independent, and neither is
 * restated by a caller. The class string is built by `buttonClasses` so that each CSS property has
 * exactly one owner: a call site that "fixed" a size with important utilities (`h-10!`, `p-0!`)
 * was working around a missing size here, and a descendant selector like `[&_button]:` reaching
 * into another component's button is the same mistake one level up. Add the size instead.
 *
 * **As a link.** Pass `to` and the same box renders as a `RouterLink` (a real `<a>`: role
 * `link`, middle-click, "open in new tab", and navigation that the router owns) instead of a
 * `<button>`. It exists so a screen that offers "go there" with a button's weight says what it is
 * to assistive technology rather than hand-rolling a second copy of the variant classes that would
 * drift from this one. `type` and `disabled` are ignored on a link: an `<a>` has neither, and a
 * link that looks disabled but still navigates is a lie — a caller that needs the unavailable
 * state renders no link at all (and says why in text), never a dead-looking one. Hover styles are
 * written `not-disabled:hover:` rather than `enabled:hover:` because `:enabled` never matches an
 * `<a>`, which would leave the link with no hover at all. A link still emits `click` (before the
 * router navigates), so a caller that must react to the activation — closing the drawer it sits
 * in, say — does not have to fall back to a button to be told.
 *
 * **Focus.** `shadow-focus` (`shadow-focus-danger` on a destructive button) on
 * `focus-visible` only, so it reaches keyboard users without outlining every
 * mouse click. It replaces the primary's glow while focused, which is the
 * intent: the ring has to be the loudest thing on the control.
 */
import { computed, type ComputedRef } from 'vue'
import { RouterLink, type RouteLocationRaw } from 'vue-router'
import { buttonClasses } from '../../utils/buttonClasses'

/**
 * Visual weight of a {@link UiButton}, one per button the design draws.
 * Declared here rather than in `src/types/` because it describes this
 * component's props and means nothing without it (rules/vue.md).
 */
export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'quiet' | 'field' | 'destructive' | 'ai'

/**
 * Geometry of a {@link UiButton}, independent of its variant.
 *
 * - `md`: the default, a labelled action at least 40px tall.
 * - `bar`: a 40px-tall app-bar action that holds an icon with an optional label beside it.
 * - `icon`: a 40px square holding one icon, for a control with no visible text.
 * - `icon-sm`: the same at 32px, for a control tucked into a panel's corner.
 * - `tab`: a stacked icon-over-label cell, for a tab bar.
 *
 * An `icon` or `icon-sm` button has no text, so its caller MUST pass an `aria-label`; both are
 * at least 32x32 CSS pixels, above the 24x24 minimum for a pointer target.
 */
export type ButtonSize = 'md' | 'bar' | 'icon' | 'icon-sm' | 'tab'

/** Props accepted by {@link UiButton}. */
const props = withDefaults(
  defineProps<{
    /** Visual weight: `primary` for the main action, `secondary` for a bordered alternative, `ghost` for a low-emphasis action (e.g. a nav-adjacent control), `destructive` for an action that removes something, `ai` for an assistant-initiated action. */
    variant?: ButtonVariant
    /** Geometry: `md` (default) for a labelled action, `bar` for an app-bar action, `icon` / `icon-sm` for an icon-only square (needs an `aria-label`), `tab` for a stacked tab-bar cell. */
    size?: ButtonSize
    /** Native `type` attribute; defaults to `button` so it never submits a form by accident. */
    type?: 'button' | 'submit'
    /** Disables the button and marks it non-interactive for assistive tech. Ignored when `to` is set: a link cannot be disabled. */
    disabled?: boolean
    /**
     * Destination, passed straight to `RouterLink`'s `to`. When set the component renders a link
     * (role `link`, navigates) with the same variant and size styling instead of a `<button>`, and
     * `type` and `disabled` have no effect.
     */
    to?: RouteLocationRaw
  }>(),
  { variant: 'primary', size: 'md', type: 'button', disabled: false, to: undefined },
)

/** Events emitted by {@link UiButton}. */
const emit = defineEmits<{
  /** Fired on a non-disabled click. */
  (e: 'click', payload: MouseEvent): void
}>()

/**
 * The button's complete class string, built from its variant and size by {@link buttonClasses}.
 * Each CSS property is set by exactly one of base, variant and size, so a caller never needs an
 * important modifier to win it back; the same string styles the `<button>` and the link, so the
 * two can never differ in size or shape. The `disabled:` utilities are inert on an `<a>`, which is
 * intended (see "As a link" above).
 */
const classes: ComputedRef<string> = computed(() => {
  return buttonClasses(props.variant, props.size)
})

/**
 * Forwards a native click to the `click` emit, unless the button is disabled. A link ignores
 * `disabled` (see "As a link"), so it always forwards.
 * @param event The native mouse event.
 * @returns Nothing; re-emits synchronously.
 */
const onClick = (event: MouseEvent): void => {
  if (props.disabled && props.to === undefined) {
    return
  }
  emit('click', event)
}
</script>

<template>
  <!-- `border` is on every variant so switching variant never shifts the box by
       two pixels; it is transparent where the variant draws none. The disabled
       look is shared and wins over the variant's: a flat raised surface, muted
       text, no glow, so a disabled pill cannot be mistaken for a live one. A
       link has no disabled state, so it takes the same classes and none of the
       `type`/`disabled` attributes. -->
  <RouterLink v-if="to !== undefined" :to="to" :class="classes" @click="onClick">
    <slot />
  </RouterLink>
  <button v-else :type="type" :disabled="disabled" :class="classes" @click="onClick">
    <slot />
  </button>
</template>
