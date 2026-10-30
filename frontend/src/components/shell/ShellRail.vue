<script setup lang="ts">
/**
 * The shell's collapsed sidebar: the same backend-driven entries as {@link ShellSidebar},
 * drawn icon-only in a 56px rounded panel, with the expand control at the foot. The layout
 * renders exactly one of the two at a time, and only on desktop — below 1024px the sidebar
 * is a drawer and a narrow strip beside a phone-width page would be no better than the sidebar
 * it replaced.
 *
 * **Each entry keeps its text as screen-reader-only content**, not only an `aria-label`: the
 * accessible name then survives translation, the link has real content for assistive
 * technology, and a locked entry can say "Locked" in the same breath.
 *
 * **Flyout (issue #195).** Hovering or focusing an icon shows a flyout beside the rail with the
 * entry's title and, for an entry with a submenu, its children as real links. It replaces the bare
 * `title` tooltip, so the collapsed rail still reaches every child. The flyout is rendered right
 * after the icon in the DOM (not teleported), so Tab moves from the icon into its children and on
 * to the next icon; it is `position: fixed` at the icon's measured position, which is what lets it
 * escape the rail's scroll clipping. It closes when the pointer and focus have both left the entry
 * (a short grace delay lets the pointer cross the gap) and on `Escape`, which returns focus to the
 * icon. A flyout with no children is a visual label only and is `aria-hidden`: the link already
 * carries its name.
 *
 * The list scroller spans the panel's full width with its padding inside it, so the active
 * square's glow is clipped at the panel edge rather than at the square, and it never scrolls
 * sideways (the screen-reader-only labels are absolutely positioned).
 *
 * Active and hover states are drawn by `UiNavLink`; this component adds no colour of its own.
 */
import { onBeforeUnmount, ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiNav from '../ui/UiNav.vue'
import UiNavItem from '../ui/UiNavItem.vue'
import UiNavLink from '../ui/UiNavLink.vue'
import type { NavigationEntry } from '../../types/navigation'

/** Props accepted by {@link ShellRail}. */
defineProps<{
  /** Navigation entries to render, in catalogue order, as built by `useNavigation`. */
  entries: readonly NavigationEntry[]
}>()

/** Events emitted by {@link ShellRail}. */
const emit = defineEmits<{
  /** Requests that the shell expand the rail back into the full sidebar. */
  (e: 'expand'): void
}>()

/** How long the flyout survives the pointer leaving, so it can cross the gap to the flyout. */
const CLOSE_DELAY_MS = 150

/** Space between the rail's icon and the flyout, in CSS pixels. */
const FLYOUT_OFFSET_PX = 8

const { t } = useI18n()

/** Key of the entry whose flyout is open, or null. */
const flyoutKey: Ref<string | null> = ref(null)

/** Viewport position of the open flyout. */
const flyoutPosition: Ref<{ top: number; left: number }> = ref({ top: 0, left: 0 })

/** Pending close timer, cancelled when the pointer comes back. */
let closeTimer: number | undefined

/**
 * Text drawn for an entry: the shell's own entries carry an i18n key, modules a backend label.
 * @param entry The entry to name.
 * @returns The label to render.
 */
const labelOf = (entry: NavigationEntry): string => {
  return entry.labelKey === null ? (entry.label ?? '') : t(entry.labelKey)
}

/**
 * Cancels a pending close.
 * @returns Nothing.
 */
const cancelClose = (): void => {
  if (closeTimer !== undefined) {
    window.clearTimeout(closeTimer)
    closeTimer = undefined
  }
}

/**
 * Opens an entry's flyout beside the entry's row.
 * @param key The entry's key.
 * @param event The pointer or focus event whose current target is the entry's wrapper.
 * @returns Nothing.
 */
const openFlyout = (key: string, event: Event): void => {
  cancelClose()
  if (flyoutKey.value === key) {
    return
  }
  const wrapper = event.currentTarget
  if (!(wrapper instanceof HTMLElement)) {
    return
  }
  const rect = wrapper.getBoundingClientRect()
  flyoutPosition.value = { top: rect.top, left: rect.right + FLYOUT_OFFSET_PX }
  flyoutKey.value = key
}

/**
 * Closes the flyout after the grace delay.
 * @returns Nothing.
 */
const scheduleClose = (): void => {
  cancelClose()
  closeTimer = window.setTimeout((): void => {
    flyoutKey.value = null
    closeTimer = undefined
  }, CLOSE_DELAY_MS)
}

/**
 * Closes the flyout once focus has left the entry and everything inside its flyout.
 * @param event The focusout event from the entry's wrapper.
 * @returns Nothing.
 */
const onFocusOut = (event: FocusEvent): void => {
  const wrapper = event.currentTarget
  const next = event.relatedTarget
  if (wrapper instanceof HTMLElement && next instanceof Node && wrapper.contains(next)) {
    return
  }
  cancelClose()
  flyoutKey.value = null
}

/**
 * `Escape` closes the open flyout and puts focus back on the entry's icon.
 * @param event The keydown event from the entry's wrapper.
 * @returns Nothing.
 */
const onKeydown = (event: KeyboardEvent): void => {
  if (event.key !== 'Escape' || flyoutKey.value === null) {
    return
  }
  const wrapper = event.currentTarget
  // Focus first: moving it fires `focusin`, which would reopen the flyout if it were closed already.
  if (wrapper instanceof HTMLElement) {
    wrapper.querySelector<HTMLElement>('a')?.focus()
  }
  cancelClose()
  flyoutKey.value = null
}

/**
 * Asks the shell to expand the rail. The collapsed/expanded state lives in the layout, which
 * owns both presentations.
 * @returns Nothing; the event is emitted synchronously.
 */
const expand = (): void => {
  emit('expand')
}

onBeforeUnmount(cancelClose)
</script>

<template>
  <aside
    class="flex w-14 flex-none flex-col gap-2.5 rounded-panel border border-border-subtle bg-surface-1 py-2.5"
  >
    <div class="min-h-0 flex-1 overflow-x-hidden overflow-y-auto px-2.5">
      <UiNav :label="t('app.nav.ariaLabel')">
        <UiNavItem v-for="entry in entries" :key="entry.key">
          <!-- Pointer and focus listeners only decide whether the flyout is shown; every control
               inside is a real link that works without them. -->
          <div
            @mouseenter="openFlyout(entry.key, $event)"
            @mouseleave="scheduleClose"
            @focusin="openFlyout(entry.key, $event)"
            @focusout="onFocusOut"
            @keydown="onKeydown"
          >
            <UiNavLink :to="entry.target" :locked="entry.locked" compact>
              <UiIcon :name="entry.icon" size="md" />
              <span class="sr-only">
                {{ labelOf(entry) }}
                <template v-if="entry.locked">{{ t('app.nav.lockedBadge') }}</template>
              </span>
            </UiNavLink>

            <div
              v-if="flyoutKey === entry.key"
              class="fixed z-40 min-w-44 rounded-control border border-border-subtle bg-surface-1 p-1.5 shadow-glow"
              :style="{ top: `${flyoutPosition.top}px`, left: `${flyoutPosition.left}px` }"
              :aria-hidden="entry.children.length === 0 ? 'true' : undefined"
            >
              <p class="px-2.5 py-1 text-sm font-semibold text-text-primary">
                {{ labelOf(entry) }}
              </p>
              <ul
                v-if="entry.children.length > 0"
                class="flex flex-col gap-0.5"
                :aria-label="labelOf(entry)"
              >
                <UiNavItem v-for="child in entry.children" :key="child.key">
                  <UiNavLink :to="child.target" nested>
                    <span class="min-w-0 wrap-anywhere">{{ labelOf(child) }}</span>
                  </UiNavLink>
                </UiNavItem>
              </ul>
            </div>
          </div>
        </UiNavItem>
      </UiNav>
    </div>

    <!-- One control at the foot of the rail: expand. The theme and the palette live in the app
         bar, and a copy here would only add a button to a column that exists to be narrow. -->
    <UiButton
      variant="quiet"
      size="icon-sm"
      class="mx-auto"
      :aria-label="t('app.shell.expandSidebar')"
      :title="t('app.shell.expandSidebar')"
      @click="expand"
    >
      <UiIcon name="chevronRight" size="md" />
    </UiButton>
  </aside>
</template>
