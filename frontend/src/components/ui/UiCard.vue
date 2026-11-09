<script setup lang="ts">
/**
 * The panel of the redesign: a bordered, padded container that groups related
 * information. It lays out its default slot, and a caller adds spacing
 * (`class="mb-4"`) rather than the card baking in a margin its call sites would
 * have to undo.
 *
 * It is flat at rest: the surface and a subtle border mark it off, with no
 * shadow, so depth comes from the surface ramp. Without `to` it carries NO hover
 * treatment: a border that strengthens on hover tells the user the card is
 * clickable, and on a static card that is a lie — it invites a click that does
 * nothing.
 *
 * **With `to` the whole card is one link.** It renders a `RouterLink` (role
 * `link`, reachable by keyboard, named by its content), lifts 2px and takes an
 * accent border on hover, and shows the kit's focus ring on keyboard focus — the
 * form the dashboard's figure tiles take. A linked card's content must therefore
 * be non-interactive (no button or link inside a link); a card that needs
 * controls inside it stays a plain card and puts its link in its heading.
 *
 * `min-w-0` is load-bearing: a card is routinely a grid or flex child, and
 * without it a long unbroken value (a domain, a path, an Armenian word) forces
 * the track wider than the screen and the page scrolls sideways at 390px.
 */
import { RouterLink, type RouteLocationRaw } from 'vue-router'

/** Props accepted by {@link UiCard}. */
withDefaults(
  defineProps<{
    /** Where the whole card leads; passed to `RouterLink`. Left out, the card is a plain, non-interactive box. */
    to?: RouteLocationRaw
  }>(),
  { to: undefined },
)
</script>

<template>
  <RouterLink
    v-if="to !== undefined"
    :to="to"
    class="block min-w-0 rounded-panel border border-border-subtle bg-surface-1 p-4 text-text-primary transition duration-300 hover:-translate-y-0.5 hover:border-accent/45 focus-visible:shadow-focus focus-visible:outline-none"
  >
    <slot />
  </RouterLink>
  <div v-else class="min-w-0 rounded-panel border border-border-subtle bg-surface-1 p-4">
    <slot />
  </div>
</template>
