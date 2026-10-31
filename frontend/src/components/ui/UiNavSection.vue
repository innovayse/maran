<script setup lang="ts">
/**
 * A navigation section's own row: the link to the section's landing page and, drawn over the
 * row's end, the toggle that opens or closes its submenu (issue #195).
 *
 * **One shape for every row (issue #221).** The toggle used to sit beside the link, so a lit
 * section's pill stopped short of the toggle while its children's pills ran to the panel's edge:
 * two widths for one state. The link now spans the whole row (`UiNavLink`'s `trailing` leaves
 * room at its end) and the toggle is laid over that room, inside the pill. It cannot be inside the
 * link itself, because a button inside a link is invalid markup and two controls in one would
 * share one accessible name.
 *
 * **The toggle's colour follows the row.** On an unlit row it is `text-text-muted` (5.36:1 on a
 * panel), stepping up to primary text on the `bg-surface-3` hover, like the kit's `quiet` button.
 * On a lit row it sits on `--acf`, so it is white (5.14:1) with the `--ach` hover (6.80:1); white
 * never sits on `--ac`. That is why this is a kit primitive rather than a `UiButton` placed by
 * the sidebar: no button variant knows the row it is drawn over.
 *
 * The link and the toggle stay two controls on the Tab path. The toggle carries `aria-expanded`
 * and, while open, `aria-controls` naming the submenu; its name comes from the caller, translated.
 */
import UiIcon from './UiIcon.vue'
import UiNavLink from './UiNavLink.vue'
import type { RouteLocationRaw } from 'vue-router'

/** Props accepted by {@link UiNavSection}. */
defineProps<{
  /** The section's landing page, passed to the link. */
  to: RouteLocationRaw
  /** Whether the current page is one of this section's pages; lights the whole row. */
  current: boolean
  /** Whether the section's submenu is open. */
  open: boolean
  /** The section's key, set on the link as `data-nav-section` for keyboard movement. */
  section: string
  /** The id of the submenu the toggle controls, referenced only while it is rendered. */
  controls: string
  /** The toggle's accessible name, already translated. */
  toggleLabel: string
}>()

/** Events emitted by {@link UiNavSection}. */
const emit = defineEmits<{
  /** The section's link was followed. */
  (e: 'navigate'): void
  /** The toggle was pressed. */
  (e: 'toggle'): void
}>()

/**
 * Reports that the section's link was followed.
 * @returns Nothing; the event is emitted synchronously.
 */
const navigate = (): void => {
  emit('navigate')
}

/**
 * Reports that the toggle was pressed.
 * @returns Nothing; the event is emitted synchronously.
 */
const toggle = (): void => {
  emit('toggle')
}
</script>

<template>
  <div class="relative">
    <UiNavLink :to="to" :active="current" trailing :data-nav-section="section" @click="navigate">
      <slot />
    </UiNavLink>
    <button
      type="button"
      class="absolute top-1/2 right-1 flex size-7 -translate-y-1/2 items-center justify-center rounded-inset transition-colors focus-visible:shadow-focus focus-visible:outline-none motion-reduce:transition-none"
      :class="
        current
          ? 'text-white hover:bg-accent-hover'
          : 'text-text-muted hover:bg-surface-3 hover:text-text-primary'
      "
      :aria-label="toggleLabel"
      :aria-expanded="open ? 'true' : 'false'"
      :aria-controls="open ? controls : undefined"
      @click="toggle"
    >
      <UiIcon
        name="chevronDown"
        size="sm"
        class="transition-transform motion-reduce:transition-none"
        :class="open ? 'rotate-180' : ''"
      />
    </button>
  </div>
</template>
