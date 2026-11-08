<script setup lang="ts">
/**
 * A floating panel that opens from a trigger and holds content to read and act on, not commands to
 * pick: the app bar's notification feed is the first. It is the kit's answer to a gap between two
 * primitives. `UiDropdown` is a menu of commands (`role="menu"`, arrow keys between items), and a
 * feed with its own buttons is not one; `UiModal` takes the whole screen, and a glance at a short
 * list should not.
 *
 * **Contract.** The trigger is a real button with `aria-haspopup="dialog"`, `aria-expanded` and
 * `aria-controls`, named by `label` (a trigger that is only an icon and a badge has no other name).
 * The panel is a `role="dialog"` named by its `title`, which is also its visible heading. Focus
 * moves in on open (the shared focus trap), Tab wraps inside the panel, Escape closes, and focus
 * returns to the trigger on every close. The caller can own the state with `v-model:open`.
 *
 * **Two presentations, one DOM.** From `lg` (1024px) up the panel floats under the trigger, right
 * edge aligned with it and clamped inside the viewport, and a press outside closes it. Below `lg`
 * it is a bottom sheet over a dimmed backdrop, where a thumb reaches it, like `UiFilterSheet`; the
 * dialog is then `aria-modal`. The panel is teleported to `body` so no scrolling ancestor clips it,
 * and re-placed (not closed) when the page scrolls or the window resizes.
 *
 * **Look.** The panel is a `bg-surface-1` `rounded-control` card with a strong border and
 * `shadow-xl`, the one place a shadow is right because it floats; its head is the title and a close
 * button, its body scrolls inside `max-h-sheet` so the head stays put. Under
 * `prefers-reduced-motion` it appears and disappears at once.
 *
 * **Exposed method.** `focusPanel()` moves focus to the panel, for an action that removes the
 * control that had it.
 */
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useId, watch, type ComputedRef, type Ref } from 'vue'
import { useFocusTrap } from '../../composables/useFocusTrap'
import { useMediaQuery } from '../../composables/useMediaQuery'
import { buttonClasses } from '../../utils/buttonClasses'
import UiButton from './UiButton.vue'
import UiIcon from './UiIcon.vue'

/** Props accepted by {@link UiPopover}. */
defineProps<{
  /** The trigger's accessible name, already translated; the trigger's content is usually only an icon. */
  label: string
  /** The panel's heading and accessible name, already translated. */
  title: string
  /** The close control's accessible name, already translated. */
  closeLabel: string
}>()

/** Whether the panel is open; bind with `v-model:open` to read or drive it. */
const open: Ref<boolean> = defineModel<boolean>('open', { default: false })

/** Slots of {@link UiPopover}. */
defineSlots<{
  /** What the trigger button shows (an icon and a badge); the button itself, and its name, are the popover's. */
  trigger: () => unknown
  /** The panel's body. */
  default: () => unknown
}>()

/** Gap in CSS pixels between the trigger and the floating panel. */
const TRIGGER_GAP = 4

/** Gap in CSS pixels kept between the floating panel and the viewport edge. */
const VIEWPORT_MARGIN = 8

/** From this width up the panel floats under its trigger; Tailwind's `lg`. */
const FLOATING_QUERY = '(min-width: 64rem)'

/** Whether the viewport is wide enough for the floating presentation (otherwise the bottom sheet). */
const floating: Readonly<Ref<boolean>> = useMediaQuery(FLOATING_QUERY)

/** Stable, unique ids tying the trigger, the panel and its heading together. */
const triggerId: string = useId()
const panelId: string = `${triggerId}-panel`
const titleId: string = `${triggerId}-title`

/** The component's root, with the trigger in it. */
const rootElement: Ref<HTMLElement | null> = ref(null)

/** The trigger button, which focus returns to on close. */
const triggerElement: Ref<HTMLButtonElement | null> = ref(null)

/** The dialog panel, whose focusable descendants the trap cycles through. */
const panelElement: Ref<HTMLElement | null> = ref(null)

/** The floating panel's viewport coordinates. */
const offset: Ref<{ top: number; left: number }> = ref({ top: 0, left: 0 })

/** Classes of the trigger: the kit's quiet icon button, positioned so a badge can sit on it. */
const triggerClasses: string = `relative ${buttonClasses('quiet', 'icon')}`

/** Inline placement of the floating panel; the sheet is placed by its classes alone. */
const panelStyle: ComputedRef<Record<string, string> | undefined> = computed(() => {
  if (!floating.value) {
    return undefined
  }
  return {
    top: `${offset.value.top}px`,
    left: `${offset.value.left}px`,
  }
})

/**
 * Puts the floating panel under the trigger with its right edge on the trigger's, clamped so a
 * trigger near an edge cannot push it out of the viewport.
 * @returns Nothing; the offset updates synchronously.
 */
const place = (): void => {
  const trigger = triggerElement.value
  const panel = panelElement.value
  if (trigger === null || panel === null) {
    return
  }
  const triggerRect = trigger.getBoundingClientRect()
  const width = panel.getBoundingClientRect().width
  const furthest = window.innerWidth - width - VIEWPORT_MARGIN
  offset.value = {
    top: triggerRect.bottom + TRIGGER_GAP,
    left: Math.max(VIEWPORT_MARGIN, Math.min(triggerRect.right - width, furthest)),
  }
}

/**
 * Toggles the panel from the trigger.
 * @returns Nothing.
 */
const toggle = (): void => {
  open.value = !open.value
}

/**
 * Closes the panel; the focus trap hands focus back to the trigger.
 * @returns Nothing.
 */
const close = (): void => {
  open.value = false
}

/**
 * Takes focus for the trigger on a pointer press, in place of the browser's own default. Safari
 * does not focus a button on click, and the focus trap returns focus to whatever had it when the
 * panel opened, so without this the trigger would not get it back there.
 * @returns Nothing; focus moves synchronously.
 */
const onTriggerMouseDown = (): void => {
  triggerElement.value?.focus({ preventScroll: true })
}

/**
 * Closes a bottom sheet on a click on its backdrop itself, never on one that bubbled out of the panel.
 * @param event The click on the backdrop.
 * @returns Nothing.
 */
const onBackdropClick = (event: MouseEvent): void => {
  if (!floating.value && event.target === event.currentTarget) {
    close()
  }
}

/**
 * Closes the floating panel on a press outside both the trigger and the panel. Bound to
 * `mousedown` so the panel is gone before the outside target reacts.
 * @param event The document-level pointer event.
 * @returns Nothing.
 */
const onDocumentMouseDown = (event: MouseEvent): void => {
  if (!open.value || !floating.value) {
    return
  }
  const target = event.target
  if (target instanceof Node && (rootElement.value?.contains(target) === true || panelElement.value?.contains(target) === true)) {
    return
  }
  close()
}

/**
 * Re-places an open floating panel after a resize or a scroll moved its trigger.
 * @returns Nothing.
 */
const onViewportChange = (): void => {
  if (open.value && floating.value) {
    place()
  }
}

/**
 * Moves focus to the panel itself. A caller whose action removes the control that had focus (a
 * dismissed row) uses it so focus stays inside the dialog instead of falling to the document body.
 * @returns Nothing; focus moves synchronously.
 */
const focusPanel = (): void => {
  panelElement.value?.focus({ preventScroll: true })
}

defineExpose({ focusPanel })

const { onTab } = useFocusTrap({ panel: panelElement, open: (): boolean => {return open.value} })

watch(
  open,
  async (isOpen: boolean): Promise<void> => {
    if (isOpen) {
      // The panel has to exist before it can be measured. The measuring and the re-render it causes
      // both finish in microtasks, before the browser paints, so the frame at the previous
      // coordinates is never shown. (It is deliberately not hidden with `visibility`: a hidden
      // element cannot take the focus the trap moves in at the same moment.)
      await nextTick()
      place()
    }
  },
)

onMounted((): void => {
  document.addEventListener('mousedown', onDocumentMouseDown)
  window.addEventListener('resize', onViewportChange)
  window.addEventListener('scroll', onViewportChange, true)
})

onBeforeUnmount((): void => {
  document.removeEventListener('mousedown', onDocumentMouseDown)
  window.removeEventListener('resize', onViewportChange)
  window.removeEventListener('scroll', onViewportChange, true)
})
</script>

<template>
  <div ref="rootElement" class="inline-block">
    <button
      :id="triggerId"
      ref="triggerElement"
      type="button"
      aria-haspopup="dialog"
      :aria-label="label"
      :aria-expanded="open"
      :aria-controls="open ? panelId : undefined"
      :class="triggerClasses"
      @mousedown.prevent="onTriggerMouseDown"
      @click="toggle"
    >
      <slot name="trigger" />
    </button>

    <Teleport to="body">
      <Transition name="ui-popover">
        <!-- z-[60] for the sheet: the same layer as UiModal and UiFilterSheet. The floating panel
             sits at z-50, with the dropdown panels. -->
        <div
          v-if="open"
          :class="floating ? 'fixed z-50' : 'fixed inset-0 z-[60] flex items-end bg-page/70 backdrop-blur-sm'"
          :style="panelStyle"
          data-testid="popover-layer"
          @click="onBackdropClick"
          @keydown.esc.prevent="close"
          @keydown.tab="onTab"
        >
          <div
            :id="panelId"
            ref="panelElement"
            role="dialog"
            :aria-modal="!floating"
            :aria-labelledby="titleId"
            tabindex="-1"
            class="ui-popover-panel flex max-h-sheet min-w-0 flex-col overflow-hidden border border-border-strong bg-surface-1 shadow-xl focus-visible:outline-none"
            :class="floating ? 'w-96 rounded-control' : 'w-full rounded-t-panel'"
            data-testid="popover"
          >
            <div class="flex shrink-0 items-center justify-between gap-3 border-b border-border-subtle px-4 py-2">
              <h2 :id="titleId" class="min-w-0 font-display text-base font-extrabold text-text-primary wrap-anywhere">
                {{ title }}
              </h2>
              <UiButton variant="quiet" size="icon" :aria-label="closeLabel" @click="close">
                <UiIcon name="x" size="sm" />
              </UiButton>
            </div>
            <div class="min-h-0 flex-1 overflow-y-auto wrap-anywhere">
              <slot />
            </div>
          </div>
        </div>
      </Transition>
    </Teleport>
  </div>
</template>

<style scoped>
/* The layer fades in, and leaving is quicker. */
.ui-popover-enter-active {
  transition: opacity 150ms ease-out;
}

.ui-popover-leave-active {
  transition: opacity 100ms ease-in;
}

.ui-popover-enter-from,
.ui-popover-leave-to {
  opacity: 0;
}

@media (prefers-reduced-motion: reduce) {
  .ui-popover-enter-active,
  .ui-popover-leave-active {
    transition: none;
  }
}
</style>
