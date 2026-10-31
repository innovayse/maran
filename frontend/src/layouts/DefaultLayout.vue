<script setup lang="ts">
/**
 * Authenticated shell: a flat dark desktop with rounded panels floating on it, 12px apart — the
 * app bar on top, the navigation (sidebar, or its collapsed rail) on the left, one content panel.
 * It also owns the document's single `<main>` landmark wrapping the routed page (rules/vue.md:
 * "Exactly one `<main>` per document" — this layout is the one place that renders it; pages
 * render `<section>`). Every route that belongs to the authenticated area nests under this layout
 * in the router.
 *
 * **The content panel is the only thing that scrolls.** The frame is exactly the viewport tall
 * and clips; `<main>` is the scroller, so the app bar and the navigation stay put while a long
 * page moves under them. `h-dvh` rather than `h-screen` so a phone's collapsing address bar does
 * not push the tab bar below the visible area.
 *
 * **Below 1024px the sidebar is a drawer** ({@link ShellDrawer}) and a bottom tab bar appears
 * ({@link ShellTabBar}). The two presentations are different components, so the viewport is read
 * in JavaScript rather than hidden by CSS: rendering both would put two navigation landmarks and
 * two copies of every link in the document. The match is read synchronously in setup, not on
 * mount, so a phone never paints one frame of desktop sidebar before the swap.
 *
 * **While the drawer is open the rest of the shell is `inert`.** The drawer is a modal dialog:
 * the page behind it must not take focus, be clicked through, or be read by assistive technology.
 * The drawer itself sits outside the inert frame, which is why the frame is a sibling of it and
 * not its parent.
 *
 * **The first Tab stop is a "Skip to content" link** (WCAG 2.4.1 Bypass Blocks). Without it a
 * keyboard user crosses the logo and every sidebar entry on every screen before reaching the page.
 * The link is visually hidden until it takes focus, then drawn as a labelled chip with the shell's
 * focus ring, and activating it moves focus into `<main>`. `<main>` is given `tabindex="-1"` so it
 * can receive that focus without joining the Tab order. The link handles the click itself instead
 * of navigating to a `#main-content` fragment: in history mode a fragment would rewrite the URL
 * and push an entry the user then has to Back out of.
 */
import {
  computed,
  onBeforeUnmount,
  onMounted,
  provide,
  ref,
  shallowRef,
  useTemplateRef,
  watch,
  type ComputedRef,
  type Ref,
} from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import ShellCommandPalette from '../components/shell/ShellCommandPalette.vue'
import ShellDrawer from '../components/shell/ShellDrawer.vue'
import ShellHeader from '../components/shell/ShellHeader.vue'
import ShellRail from '../components/shell/ShellRail.vue'
import ShellSidebar from '../components/shell/ShellSidebar.vue'
import ShellTabBar from '../components/shell/ShellTabBar.vue'
import { useNavigation } from '../composables/useNavigation'
import { useLocaleStore } from '../stores/locale'
import { useModulesStore } from '../stores/modules'
import { FALLBACK_GROUP } from '../utils/groupNavigation'
import { locateNavigation } from '../utils/locateNavigation'
import { PAGE_EYEBROW_KEY } from '../utils/pageEyebrowKey'
import { PAGE_HEADING_KEY } from '../utils/pageHeadingKey'
import type { PageHeading } from '../types/pageHeading'

/**
 * The viewport at which the sidebar can afford a column of its own: Tailwind's `lg`, `64rem`
 * (1024px), written as the `min-width` media query that `lg:` itself compiles to. Below it the
 * 240px sidebar eats most of a phone screen, so the shell moves it into a drawer instead.
 *
 * This is the ONE place the shell asks the question. The app bar's menu button and the brand
 * cell's width take the answer as a prop (`compact`) rather than repeating the breakpoint as an
 * `lg:` class, so the layout and the chrome cannot disagree about which presentation is showing.
 */
const WIDE_QUERY = '(min-width: 64rem)'

const { t } = useI18n()
const route = useRoute()
const entries = useNavigation()
const modulesStore = useModulesStore()
const localeStore = useLocaleStore()

/** Live match for {@link WIDE_QUERY}; kept so its listener can be removed. */
const wideQuery: MediaQueryList = window.matchMedia(WIDE_QUERY)

/** Whether the viewport is too narrow for a sidebar in the flow. */
const isCompact: Ref<boolean> = ref(!wideQuery.matches)

/** Whether the navigation drawer is open. Only meaningful while compact. */
const drawerOpen: Ref<boolean> = ref(false)

/**
 * Whether the navigation is collapsed to the icon rail. The design keeps both presentations and
 * switches between them, so this is the shell's own state rather than a preference the panel
 * reports.
 */
const collapsed: Ref<boolean> = ref(false)

/** Whether the jump-to palette is open. */
const paletteOpen: Ref<boolean> = ref(false)

/**
 * The page eyebrow: the navigation group the current screen is drawn under, read from the same
 * sources as the sidebar so the two cannot disagree (issue #209). A screen outside the navigation
 * takes the neutral fallback group.
 */
const eyebrow: ComputedRef<string> = computed(() => {
  return t(`app.nav.groups.${locateNavigation(route)?.groupKey ?? FALLBACK_GROUP}`)
})

/**
 * The page heading on screen, which the breadcrumb ends a record screen's trail on. Shallow on
 * purpose: a heading withdraws its report by comparing it with what it wrote, and a deep ref would
 * hand back a proxy that never compares equal.
 */
const pageHeading: Ref<PageHeading | null> = shallowRef(null)

provide(PAGE_EYEBROW_KEY, eyebrow)
provide(PAGE_HEADING_KEY, pageHeading)

/** The `<main>` element, the target the skip link moves focus to. */
const mainElement = useTemplateRef<HTMLElement>('main')

/**
 * Moves focus into `<main>` for the skip link. Focus (not scroll alone) is what matters: the next
 * Tab continues from the page's first control instead of from the link at the top of the shell.
 * @returns Nothing; focus moves synchronously.
 */
const skipToContent = (): void => {
  mainElement.value?.focus()
}

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
 * Records the compact state when the viewport crosses the breakpoint.
 * @param event The match state of {@link WIDE_QUERY}.
 * @returns Nothing; state updates synchronously.
 */
const applyCompact = (event: MediaQueryListEvent): void => {
  isCompact.value = !event.matches
  // Growing back to desktop must not leave the drawer "open" over a sidebar that is in the flow
  // again — the inert frame and the backdrop would block the whole page.
  if (event.matches) {
    drawerOpen.value = false
  }
}

/**
 * Toggles the palette on the platform's own shortcut.
 *
 * Both modifiers are accepted: the design shows the macOS command key, and this panel is
 * administered from Linux servers at least as often, where the same gesture is Ctrl.
 * `preventDefault` stops the browser's own find-in-page. The shortcut is ignored while the
 * drawer is open: the palette would open over a page the drawer has made inert.
 * @param event The keyboard event to inspect.
 * @returns Nothing; state updates synchronously.
 */
const onShortcut = (event: KeyboardEvent): void => {
  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
    event.preventDefault()
    if (!drawerOpen.value) {
      paletteOpen.value = !paletteOpen.value
    }
  }
}

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

onMounted((): void => {
  window.addEventListener('keydown', onShortcut)
  wideQuery.addEventListener('change', applyCompact)
})

onMounted(ensureCatalogueLoaded)

// The module names in the navigation and the breadcrumb are the panel's own, localized for the
// language the catalogue was READ in (issue #221). Switching the language live re-reads it, so the
// sidebar never keeps the previous language's names until the next sign-in. The previous catalogue
// stays on screen until the new one arrives (the store keeps it on failure too), so the switch
// never blanks the navigation. Wired here, in the view, because one store must not import another
// (rules/vue.md, "State").
watch(
  (): string => {
    return localeStore.current
  },
  async (): Promise<void> => {
    if (modulesStore.isLoaded) {
      await modulesStore.load()
    }
  },
)

onBeforeUnmount((): void => {
  window.removeEventListener('keydown', onShortcut)
  wideQuery.removeEventListener('change', applyCompact)
})
</script>

<template>
  <!-- `relative` on the frame and on `<main>` (issue #221): an absolutely positioned element with
       no positioned ancestor (a screen-reader-only label is one) is placed against the document,
       not clipped by `overflow: hidden`, so a long page's `sr-only` text far down the content
       made the DOCUMENT scroll too. The Firewall page showed two scrollbars, and the page
       scrollbar's width pushed the app bar sideways. With a positioned frame and scroller, such
       an element stays inside the region that clips or scrolls it. -->
  <div class="relative h-dvh overflow-hidden bg-page text-text-primary">
    <!-- First in the DOM, so first in the Tab order. `sr-only` hides it without removing it from
         the order; `focus:not-sr-only` brings it back on focus, fixed over the app bar, with the
         shell's own focus ring. It sits OUTSIDE the inert frame below on purpose: the drawer
         makes that frame inert while it is open, and a skip link must not vanish with it. -->
    <a
      href="#main-content"
      class="sr-only z-50 rounded-control bg-surface-1 px-4 py-2 text-sm font-semibold text-text-primary focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:shadow-focus focus:outline-none"
      @click.prevent="skipToContent"
    >
      {{ t('app.shell.skipToContent') }}
    </a>

    <!-- The desktop: panels floating 12px apart (8px on a phone). `inert` while the drawer is
         open makes the drawer genuinely modal. -->
    <div class="flex h-full flex-col gap-2 p-2 sm:gap-3 sm:p-3" :inert="drawerOpen">
      <ShellHeader
        :entries="entries"
        :navigation-open="drawerOpen"
        :compact="isCompact"
        @open-navigation="openDrawer"
        @open-palette="openPalette"
      />

      <div class="flex min-h-0 flex-1 gap-3">
        <template v-if="!isCompact">
          <ShellRail v-if="collapsed" :entries="entries" @expand="expandNavigation" />
          <ShellSidebar v-else :entries="entries" @collapse="collapseNavigation" />
        </template>

        <!-- The content inset lives here rather than in each page: every screen gets the same
             wrapper, and many pages repeating it is that many chances for one to drift. Pages
             render their own content flush inside it. It steps with the viewport (16, 24, 32px)
             because horizontal space is the scarce thing on a phone and the opposite on a desktop
             monitor, where an operator spends hours.
             `overflow-x-hidden` is the backstop for the rule that nothing scrolls sideways at
             390px; wide tables scroll inside their own container, not here.
             `scroll-py-3` keeps the browser's focus-driven scroll-into-view from pinning a control
             flush against this region's top or bottom edge: the 4px focus ring is drawn outside the
             control and the region clips it, so a flush control lost its ring on one side (the last
             row of the cron form, and any control reached by Tab on a phone). 12px clears it.
             `scrollbar-gutter-stable` reserves the scrollbar's lane on every page (issue #221), so
             a page long enough to scroll is laid out at the same width as one that is not. -->
        <main
          id="main-content"
          ref="main"
          tabindex="-1"
          class="relative min-w-0 flex-1 overflow-x-hidden overflow-y-auto scrollbar-gutter-stable rounded-panel border border-border-subtle bg-surface-1 p-4 scroll-py-3 focus:outline-none sm:p-6 lg:p-8"
        >
          <!-- RouterView, not a slot: this layout is a route component with child routes, so the
               page for the active child renders here. A <slot/> would silently render nothing. -->
          <RouterView />
        </main>
      </div>

      <ShellTabBar
        v-if="isCompact"
        :entries="entries"
        :navigation-open="drawerOpen"
        @open-navigation="openDrawer"
      />
    </div>

    <ShellDrawer v-if="isCompact" :open="drawerOpen" @close="closeDrawer">
      <ShellSidebar :entries="entries" compact @navigate="closeDrawer" />
    </ShellDrawer>

    <ShellCommandPalette :open="paletteOpen" :entries="entries" @close="closePalette" />
  </div>
</template>
