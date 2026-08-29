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
    class="flex items-center justify-between gap-2 rounded-md px-3 py-2 text-sm font-medium text-slate-700 transition-colors hover:bg-slate-100 aria-[current=page]:bg-slate-100 aria-[current=page]:text-slate-900"
    :aria-disabled="locked ? 'true' : undefined"
  >
    <span><slot /></span>
    <slot name="badge" />
  </RouterLink>
</template>
