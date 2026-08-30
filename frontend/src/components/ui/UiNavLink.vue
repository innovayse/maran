<script setup lang="ts">
/**
 * Accessible router link styled as navigation: a real `<a>` (via
 * `RouterLink`) so browser navigation, middle-click and screen readers all
 * work, with an optional locked state for licence-gated entries that must
 * stay visible rather than disappear (rules/architecture.md: a disabled
 * module's routes resolve to the upgrade page, never a blank screen).
 * Used both by the sidebar (`useNavigation` entries) and by plain
 * in-content "go here" links (e.g. the 404 page's way back).
 */
import type { RouteLocationRaw } from 'vue-router'

/** Props accepted by {@link UiNavLink}. */
defineProps<{
  /** Destination, passed straight to `RouterLink`'s `to`. */
  to: RouteLocationRaw
  /** Marks the entry as licence-locked: still navigable (to the upgrade page), but visually and semantically flagged. */
  locked?: boolean
}>()
</script>

<template>
  <RouterLink
    :to="to"
    class="flex w-full items-center justify-between gap-2 rounded-md px-2 py-1 text-xs font-normal text-text-secondary transition-colors hover:bg-surface-3 hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none aria-[current=page]:bg-accent-soft aria-[current=page]:font-semibold aria-[current=page]:text-accent"
    :aria-disabled="locked ? 'true' : undefined"
  >
    <span><slot /></span>
    <slot name="badge" />
  </RouterLink>
</template>
