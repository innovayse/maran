<script setup lang="ts">
/**
 * An in-content link to another screen of the panel: "Back to sites", "Back to the status page".
 *
 * It exists because a way back is navigation, and navigation has to LOOK like a link — underlined,
 * in the accent text colour — or a reader takes it for a caption (#218). Five screens had grown a
 * hand-typed copy of the same `RouterLink` class string to get there; one primitive keeps the look
 * in one place, so a change to it cannot be half-applied.
 *
 * Not {@link UiButton} with `to`: that draws a button's box around a destination, which is right
 * for a primary "go here" action and wrong for a quiet sentence-level way back. Not
 * {@link UiNavLink}, which is a navigation row with an active state. Not {@link UiExternalLink},
 * which leaves the panel in a new tab.
 *
 * **Look.** `text-accent-text` (the accent for text on the dark surfaces) at the body step, underlined
 * with a 4px offset; the underline drops on hover so the pointer gets feedback that is not colour
 * alone. The focus ring is the shared `shadow-focus`, keyboard-only. An optional leading or trailing
 * glyph is drawn through {@link UiIcon} and is decorative: the text names the destination.
 */
import { RouterLink, type RouteLocationRaw } from 'vue-router'
import UiIcon, { type UiIconName } from './UiIcon.vue'

/** Props accepted by {@link UiLink}. */
withDefaults(
  defineProps<{
    /** Where the link goes, as the router understands it. */
    to: RouteLocationRaw
    /** A decorative leading glyph, such as `chevronLeft` on a way back. */
    icon?: UiIconName
    /** A decorative trailing glyph, such as `chevronRight` on a way onward. */
    trailingIcon?: UiIconName
  }>(),
  { icon: undefined, trailingIcon: undefined },
)
</script>

<template>
  <RouterLink
    :to="to"
    class="inline-flex items-center gap-1 self-start rounded-control text-sm font-medium text-accent-text underline underline-offset-4 hover:no-underline focus-visible:shadow-focus focus-visible:outline-none"
  >
    <UiIcon v-if="icon !== undefined" :name="icon" size="sm" />
    <slot />
    <UiIcon v-if="trailingIcon !== undefined" :name="trailingIcon" size="sm" />
  </RouterLink>
</template>
