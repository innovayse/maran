<script setup lang="ts">
/**
 * Authenticated shell: the sidebar (or its collapsed rail), the header, and
 * the document's single `<main>` landmark wrapping the routed page
 * (rules/vue.md: "Exactly one `<main>` per document" — this layout is the one
 * place that renders it; pages render `<section>`). Every route that belongs
 * to the authenticated area nests under this layout in the router.
 *
 * The frame follows the design canvas: a full-height flex row that never
 * scrolls itself, with only `<main>` scrolling, so the sidebar and header
 * stay put while a long page moves under them.
 */
import { nextTick, onBeforeUnmount, onMounted, ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import ShellHeader from '../components/shell/ShellHeader.vue'
import ShellCommandPalette from '../components/shell/ShellCommandPalette.vue'
import ShellRail from '../components/shell/ShellRail.vue'
import ShellSidebar from '../components/shell/ShellSidebar.vue'
import { useNavigation } from '../composables/useNavigation'
import { useModulesStore } from '../stores/modules'
import { useThemeStore } from '../stores/theme'
import { focusableElements } from '../utils/focusableElements'

/**
 * Width at which the sidebar can afford to hold a column of its own — Tailwind's
 * `lg`. Below it the 246px sidebar eats most of a phone screen, so the shell
 * moves it off-canvas instead. The number is duplicated as `lg:` variants in
 * {@link ../components/shell/ShellHeader.vue}, which hides the menu button above
 * the same width; both must move together.
 *
 * The design canvas is a 1440x900 desktop artboard with no media query and no
 * narrow screen, so this breakpoint and everything it triggers is our decision,
 * not a value copied from the design.
 */
const COMPACT_QUERY = '(max-width: 1023.98px)'

const { t } = useI18n()
const entries = useNavigation()
const modulesStore = useModulesStore()
// Instantiated here rather than in `main.ts`: creating the store applies the
// persisted theme to `<html data-theme>`, and this layout is the first thing
// rendered inside the authenticated area.
const themeStore = useThemeStore()

/**
 * Whether the viewport is too narrow for a sidebar in the flow. Reactive
 * rather than CSS-only because the two presentations are different components,
 * and rendering both would put two `h1`s and two nav landmarks in the document.
 */
const isCompact: Ref<boolean> = ref(false)

/** Whether the off-canvas navigation drawer is open. Only meaningful while compact. */
const drawerOpen: Ref<boolean> = ref(false)

/** The drawer panel, whose focusable descendants the trap cycles through. */
const drawerElement: Ref<HTMLElement | null> = ref(null)

/** The control that opened the drawer, so focus can return to it on close. */
const drawerOpener: Ref<HTMLElement | null> = ref(null)

/** Live match for {@link COMPACT_QUERY}; kept so its listener can be removed. */
let compactQuery: MediaQueryList | null = null

/**
 * Whether the navigation is collapsed to the icon rail. The design keeps both
 * presentations and switches between them, so this is the shell's own state
 * rather than a preference the panel reports.
 */
const collapsed: Ref<boolean> = ref(false)

/** Whether the jump-to palette is open. */
const paletteOpen: Ref<boolean> = ref(false)

/**
 * Opens the jump-to palette.
 * @returns Nothing; state updates synchronously.
 */
const openPalette = (): void => {
  paletteOpen.value = true
}

/**
 * Closes the jump-to palette.
 * @returns Nothing; state updates synchronously.
 */
const closePalette = (): void => {
  paletteOpen.value = false
}

/**
 * Records the current compact state from a media-query match.
 * @param event The match state, either the initial list or a change event.
 * @returns Nothing; state updates synchronously.
 */
const applyCompact = (event: MediaQueryList | MediaQueryListEvent): void => {
  isCompact.value = event.matches
  // Growing back to desktop must not leave a drawer open over a sidebar that is
  // now in the flow again — the backdrop would block the whole page.
  if (!event.matches) {
    drawerOpen.value = false
  }
}

/**
 * Opens the navigation drawer.
 * @returns Nothing; state updates synchronously.
 */
const openDrawer = (): void => {
  drawerOpen.value = true
}

/**
 * Closes the navigation drawer.
 * @returns Nothing; state updates synchronously.
 */
const closeDrawer = (): void => {
  drawerOpen.value = false
}

/**
 * Keeps Tab inside the open drawer, wrapping at both ends. A drawer that is
 * modal for the mouse but not for the keyboard strands a keyboard user in the
 * page behind the backdrop.
 * @param event The keyboard event for Tab or Shift+Tab.
 * @returns Nothing; focus moves synchronously.
 */
const onDrawerTab = (event: KeyboardEvent): void => {
  const elements = focusableElements(drawerElement.value)
  if (elements.length === 0) {
    event.preventDefault()
    return
  }

  const first = elements[0]
  const last = elements[elements.length - 1]
  const active = document.activeElement
  if (!(active instanceof Node) || drawerElement.value?.contains(active) !== true) {
    event.preventDefault()
    first?.focus()
    return
  }

  if (event.shiftKey && (active === first || active === drawerElement.value)) {
    event.preventDefault()
    last?.focus()
    return
  }

  if (!event.shiftKey && active === last) {
    event.preventDefault()
    first?.focus()
  }
}

// Focus enters the drawer when it opens and returns to the menu button when it
// closes, so the keyboard user does not restart from the top of the document.
watch(drawerOpen, async (open: boolean): Promise<void> => {
  if (open) {
    drawerOpener.value = document.activeElement instanceof HTMLElement ? document.activeElement : null
    await nextTick()
    ;(focusableElements(drawerElement.value)[0] ?? drawerElement.value)?.focus()
    return
  }

  drawerOpener.value?.focus()
  drawerOpener.value = null
})

/**
 * Toggles the palette on the platform's own shortcut.
 *
 * Both modifiers are accepted: the design shows the macOS command key, and this
 * panel is administered from Linux servers at least as often, where the same
 * gesture is Ctrl. `preventDefault` stops the browser's own find-in-page.
 * @param event The keyboard event to inspect.
 * @returns Nothing; state updates synchronously.
 */
const onShortcut = (event: KeyboardEvent): void => {
  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
    event.preventDefault()
    paletteOpen.value = !paletteOpen.value
  }
}

onMounted((): void => {
  window.addEventListener('keydown', onShortcut)
  compactQuery = window.matchMedia(COMPACT_QUERY)
  applyCompact(compactQuery)
  compactQuery.addEventListener('change', applyCompact)
})

onBeforeUnmount((): void => {
  window.removeEventListener('keydown', onShortcut)
  compactQuery?.removeEventListener('change', applyCompact)
  compactQuery = null
})

/**
 * Ensures the module catalogue is loaded, since the navigation is built from it. The router guard
 * only fetches it for gated routes, so without this the sidebar would stay empty on every ungated
 * page — which is every page until a module ships one.
 * @returns Resolves once the catalogue request has settled.
 */
const ensureCatalogueLoaded = async (): Promise<void> => {
  if (!modulesStore.isLoaded) {
    await modulesStore.load()
  }
}

onMounted(ensureCatalogueLoaded)

/**
 * Collapses the sidebar into the rail.
 * @returns Nothing; state updates synchronously.
 */
const collapseNavigation = (): void => {
  collapsed.value = true
}

/**
 * Expands the rail back into the full sidebar.
 * @returns Nothing; state updates synchronously.
 */
const expandNavigation = (): void => {
  collapsed.value = false
}

/**
 * Flips the interface theme through the store that owns `<html data-theme>`.
 * @returns Nothing; the store updates state and the DOM synchronously.
 */
const toggleTheme = (): void => {
  themeStore.toggle()
}
</script>

<template>
  <div class="flex h-screen overflow-hidden bg-page text-text-primary">
    <!-- Compact: the sidebar leaves the flow and becomes an off-canvas drawer.
         The rail is not offered here — a 56px icon strip beside a 144px page is
         no better than the sidebar it replaces. -->
    <template v-if="isCompact">
      <div
        v-if="drawerOpen"
        class="fixed inset-0 z-40 bg-black/55 backdrop-blur-[2px]"
        aria-hidden="true"
        @click="closeDrawer"
      ></div>
      <div
        v-if="drawerOpen"
        ref="drawerElement"
        class="shell-drawer fixed inset-y-0 left-0 z-50 flex focus-visible:outline-none"
        role="dialog"
        aria-modal="true"
        tabindex="-1"
        :aria-label="t('app.shell.navigationLabel')"
        @keydown.esc.prevent="closeDrawer"
        @keydown.tab="onDrawerTab"
      >
        <ShellSidebar
          :entries="entries"
          compact
          @toggle-theme="toggleTheme"
          @open-palette="openPalette"
          @navigate="closeDrawer"
        />
      </div>
    </template>

    <!-- Wide: unchanged — the rail and the sidebar exactly as before. -->
    <template v-else>
      <!-- The rail carries no theme control: the design puts one in the expanded
           sidebar's footer and the header, not in the narrow column. -->
      <ShellRail v-if="collapsed" :entries="entries" @expand="expandNavigation" />
      <ShellSidebar
        v-else
        :entries="entries"
        @collapse="collapseNavigation"
        @toggle-theme="toggleTheme"
        @open-palette="openPalette"
      />
    </template>

    <ShellCommandPalette :open="paletteOpen" :entries="entries" @close="closePalette" />

    <div class="flex min-h-0 min-w-0 flex-1 flex-col">
      <ShellHeader :entries="entries" :navigation-open="drawerOpen" @open-navigation="openDrawer" />
      <!-- The content inset lives here rather than in each page: the design gives every
           screen the same 20px/22px/40px wrapper, and four pages repeating it is four
           chances for one to drift. Pages render their own content flush inside it. -->
      <main class="min-h-0 flex-1 overflow-y-auto px-5.5 pt-5 pb-10">
        <!-- RouterView, not a slot: this layout is a route component with child routes, so the
             page for the active child renders here. A <slot/> would silently render nothing. -->
        <RouterView />
      </main>
    </div>
  </div>
</template>

<style scoped>
/* The drawer slides in from the edge it lives on. Distance and duration only —
   a user who asked for less motion gets the panel without the travel. */
.shell-drawer {
  animation: shell-drawer-in 0.18s ease;
}

@media (prefers-reduced-motion: reduce) {
  .shell-drawer {
    animation: none;
  }
}

@keyframes shell-drawer-in {
  from {
    transform: translateX(-100%);
  }
  to {
    transform: none;
  }
}
</style>
