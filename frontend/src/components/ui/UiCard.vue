<script setup lang="ts">
/**
 * The panel of the redesign: a bordered, padded container that groups related
 * information. Takes no props and emits no events — it only lays out its
 * default slot, and a caller adds spacing (`class="mb-4"`) rather than the
 * card baking in a margin its call sites would have to undo.
 *
 * It is flat at rest: the surface and a subtle border mark it off, with no
 * shadow, so depth comes from the surface ramp. It carries NO hover treatment:
 * a border that strengthens on hover tells the user the card is clickable, and
 * on a static card that is a lie — it invites a click that does nothing. A card that IS a link is drawn by
 * the component that owns the link (it lifts 2px and takes an accent border, as
 * the dashboard's figure cards do) and uses this one only for the box.
 *
 * `min-w-0` is load-bearing: a card is routinely a grid or flex child, and
 * without it a long unbroken value (a domain, a path, an Armenian word) forces
 * the track wider than the screen and the page scrolls sideways at 390px.
 */
</script>

<template>
  <div class="min-w-0 rounded-panel border border-border-subtle bg-surface-1 p-5">
    <slot />
  </div>
</template>
