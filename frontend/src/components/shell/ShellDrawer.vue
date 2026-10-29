<script setup lang="ts">
/**
 * The phone shell's navigation drawer: the sidebar, slid in as a rounded floating panel from the
 * left with 12px of desktop around it, over the page darkened and blurred. Below 1024px this is
 * how the navigation is reached; it is a modal dialog and behaves like one.
 *
 * **What it does, each of it a rule (rules/vue.md "Accessibility"):**
 *
 * - **Closes** from its own close button, from a tap on the backdrop, from choosing a
 *   destination (the layout closes it on the sidebar's `navigate`), and from **`Escape`**. A
 *   modal that ignores the key traps a keyboard user behind its backdrop, so this one answers it.
 *   An `Escape` that something inside already handled (an open account menu, which closes
 *   itself and cancels the key) is not also taken as a request to close the drawer: that
 *   would throw away the whole navigation for one dismissed menu.
 * - **Focus moves in on open**, to the close button, and **returns on close** to whatever
 *   opened it (the hamburger or "More"), so a keyboard or screen-reader user is never left
 *   behind the backdrop or dropped at the top of the document. The opener is read from
 *   `document.activeElement` at the moment of opening, which is correct for both openers.
 *   Returning waits a tick: the layout makes the page behind the drawer `inert` while it is
 *   open, and a still-inert opener silently refuses focus.
 * - **`Tab` is held inside** while it is open. The page behind is `inert`, which already keeps
 *   focus out, but the wrap-around at each end is still needed or Tab would walk off the drawer
 *   into the browser's own chrome.
 *
 * It is a `role="dialog"` with `aria-modal` and a translated name, not a `<nav>`: the navigation
 * landmark is inside it, in the sidebar. The backdrop is hidden from assistive technology — it
 * is a click target, not content — and the close button is its keyboard equivalent.
 *
 * **Its head also holds the theme switch below `sm`.** The app bar has no room for it at phone
 * widths (see `ShellHeader`), so it lives here, in the one panel every phone-width screen can
 * open, and this copy hides again from `sm` up where the app bar shows its own. Only one of the
 * two is ever displayed, so the control is never in the accessibility tree twice.
 *
 * **The inset lives inside the sliding element.** The panel floats 12px from the screen edges, so
 * a bare `translateX(-100%)` would leave 12px of it visible. The sliding element is a transparent,
 * pointer-transparent shell whose padding IS the inset; sliding the shell by its own width
 * clears the screen exactly, with no second copy of the inset in a `calc()`, and its
 * `max-w-full` keeps the panel inside a viewport narrower than the panel.
 *
 * Motion is a slide and a fade, and `motion-reduce` removes both: a person who asked for less
 * motion gets the panel without the travel.
 */
import { onBeforeUnmount, ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import ShellThemeSwitcher from './ShellThemeSwitcher.vue'
import ShellWordmark from './ShellWordmark.vue'
import { useFocusTrap } from '../../composables/useFocusTrap'

/** Props accepted by {@link ShellDrawer}. */
const props = defineProps<{
  /** Whether the drawer is shown; owned by the layout. */
  open: boolean
}>()

/** Events emitted by {@link ShellDrawer}. */
const emit = defineEmits<{
  /** The drawer asked to close — by its button, the backdrop or `Escape`. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** The dialog panel, whose focusable descendants the Tab trap cycles through. */
const panelElement: Ref<HTMLElement | null> = ref(null)

/**
 * Asks the layout to close the drawer.
 * @returns Nothing; the event is emitted synchronously.
 */
const close = (): void => {
  emit('close')
}

/**
 * Closes the drawer on `Escape`, unless something inside it has already used the key.
 * @param event The keyboard event, listened for on the window while the drawer is open.
 * @returns Nothing; the event is emitted synchronously.
 */
const onEscape = (event: KeyboardEvent): void => {
  if (event.key === 'Escape' && !event.defaultPrevented) {
    close()
  }
}

// Focus enters on open (on the close button), Tab wraps inside the panel, and focus returns to the
// opener on close — the shared trap (composables/useFocusTrap.ts), which waits a tick before
// returning because the layout makes the page behind the drawer `inert` while it is open and a
// still-inert opener silently refuses focus.
const { onTab } = useFocusTrap({
  panel: panelElement,
  open: (): boolean => {
    return props.open
  },
  initialFocus: (): HTMLElement | null => {
    return panelElement.value?.querySelector<HTMLElement>('[data-drawer-close]') ?? null
  },
})

// `Escape` is listened for on the window only while the drawer is open, so a closed drawer costs the
// page no listener.
watch(
  (): boolean => {
    return props.open
  },
  (open: boolean): void => {
    if (open) {
      window.addEventListener('keydown', onEscape)
      return
    }
    window.removeEventListener('keydown', onEscape)
  },
)

onBeforeUnmount((): void => {
  window.removeEventListener('keydown', onEscape)
})
</script>

<template>
  <Transition
    enter-active-class="transition-opacity duration-300 motion-reduce:transition-none"
    leave-active-class="transition-opacity duration-300 motion-reduce:transition-none"
    enter-from-class="opacity-0"
    leave-to-class="opacity-0"
  >
    <!-- Darkened and blurred, as the mockup draws it. `bg-page` at 70% is the desktop colour,
         so the dimming follows the theme instead of being a hard black over a light page. -->
    <div
      v-if="open"
      class="fixed inset-0 z-40 bg-page/70 backdrop-blur-sm"
      aria-hidden="true"
      @click="close"
    ></div>
  </Transition>

  <Transition
    enter-active-class="transition-transform duration-300 ease-out motion-reduce:transition-none"
    leave-active-class="transition-transform duration-300 ease-in motion-reduce:transition-none"
    enter-from-class="-translate-x-full"
    leave-to-class="-translate-x-full"
  >
    <div v-if="open" class="pointer-events-none fixed inset-y-0 left-0 z-50 max-w-full p-3">
      <div
        id="shell-drawer"
        ref="panelElement"
        class="pointer-events-auto flex h-full w-66 max-w-full flex-col rounded-panel border border-border-subtle bg-surface-1 p-3 focus-visible:outline-none"
        role="dialog"
        aria-modal="true"
        tabindex="-1"
        :aria-label="t('app.shell.navigationLabel')"
        @keydown.tab="onTab"
      >
        <!-- The head: the wordmark, the theme switch (phone widths only) and the close button. The
             wordmark is not a heading — the page's own title is the document's `h1`. -->
        <div class="mb-2.5 flex h-11 flex-none items-center justify-between gap-2 pr-1 pl-2">
          <ShellWordmark />
          <div class="flex shrink-0 items-center gap-1">
            <div class="sm:hidden">
              <ShellThemeSwitcher />
            </div>
            <UiButton
              variant="ghost"
              size="icon"
              data-drawer-close
              :aria-label="t('app.shell.closeNavigation')"
              @click="close"
            >
              <UiIcon name="x" size="lg" />
            </UiButton>
          </div>
        </div>

        <slot />
      </div>
    </div>
  </Transition>
</template>
