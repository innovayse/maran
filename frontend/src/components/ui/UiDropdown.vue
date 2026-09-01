<script setup lang="ts">
/**
 * Menu of commands behind a trigger button — a row's "actions" control, an
 * account menu. Use it for things that DO something; a menu that only picks a
 * value is a `UiSelect`.
 *
 * Implements the ARIA menu button pattern: the trigger owns
 * `aria-haspopup="menu"`/`aria-expanded`, the panel is a `role="menu"`, and
 * real focus moves into the panel and between its items with the arrow keys
 * (unlike the listbox pattern, where focus stays on the trigger). Enter, Space
 * and Arrow Down open on the first item, Arrow Up on the last, Home and End
 * jump to the ends, Escape closes, and focus returns to the trigger on every
 * close so a keyboard user is never dropped at the top of the document.
 *
 * Items are supplied through the default slot as `UiDropdownItem` components;
 * choosing one closes the menu.
 *
 * The panel picks its own vertical side: it prefers to open downwards and flips
 * upwards when the space below the trigger cannot hold it. A menu in a sidebar
 * footer is the case that forced this — it sits against the bottom edge of the
 * viewport, so a menu that only ever opened downwards was unreachable there —
 * and the decision belongs here rather than at that one call site, because
 * every future footer menu has the same problem.
 */
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useId, type ComputedRef, type Ref } from 'vue'
import UiIcon from './UiIcon.vue'

/**
 * How the trigger is drawn. `button` is the kit's boxed control; `bare` draws
 * no box of its own, for a trigger whose slotted content already reads as a
 * control (the sidebar's identity row).
 */
export type UiDropdownVariant = 'button' | 'bare'

/** Which side of the trigger the panel is currently drawn on. */
type UiDropdownSide = 'below' | 'above'

/**
 * Gap in CSS pixels kept between the panel and the viewport edge when deciding
 * whether it fits. Small on purpose: it only has to stop the menu resting flush
 * against the edge, not to reserve a margin the design does not draw.
 */
const VIEWPORT_MARGIN = 8

/** Props accepted by {@link UiDropdown}. */
const props = withDefaults(
  defineProps<{
    /** Trigger text, already translated by the caller. */
    label: string
    /** Alignment of the panel against the trigger; `end` keeps a right-hand menu inside the viewport. */
    align?: 'start' | 'end'
    /** Disables the trigger and marks it non-interactive for assistive tech. */
    disabled?: boolean
    /**
     * Accessible name for the trigger, already translated.
     *
     * For a menu whose visible label is a value rather than a noun — a language
     * code, a selected server — where "EN" alone tells a screen-reader user
     * nothing about what pressing it does. When omitted the visible label is
     * the name, which is right for an ordinary "Actions" menu.
     */
    ariaLabel?: string
    /**
     * Draws the trailing chevron. On by default, because a plain text trigger
     * gives a sighted user nothing else to tell a menu from a button.
     *
     * Turn it off only when another mark already says "this opens something" —
     * the language picker's globe does. `aria-haspopup` carries the meaning for
     * assistive technology either way, so nothing is lost by hiding it.
     */
    chevron?: boolean
    /**
     * How the trigger is drawn. Leave it boxed unless the slotted trigger
     * content already carries its own shape.
     */
    variant?: UiDropdownVariant
  }>(),
  { align: 'start', disabled: false, ariaLabel: undefined, chevron: true, variant: 'button' },
)

/** Stable, unique ids tying the trigger to the menu it controls. */
const triggerId: string = useId()
const menuId: string = `${triggerId}-menu`

/** Whether the menu panel is currently open. */
const isOpen: Ref<boolean> = ref(false)

/** The component's outermost element, used to tell an outside click from an inside one. */
const rootElement: Ref<HTMLElement | null> = ref(null)

/** The trigger button, which focus returns to whenever the menu closes. */
const triggerElement: Ref<HTMLButtonElement | null> = ref(null)

/** The menu panel, queried for its items so slotted content needs no registration protocol. */
const menuElement: Ref<HTMLElement | null> = ref(null)

/**
 * Which side the panel is drawn on. Starts below and is re-decided from real
 * measurements every time the panel opens; it is never read from a prop,
 * because only the browser knows how much room the trigger has at that moment.
 */
const side: Ref<UiDropdownSide> = ref('below')

/** Classes positioning the trigger's box, which the `bare` variant does not draw. */
const triggerClasses: ComputedRef<string> = computed(() => {
  return props.variant === 'bare'
    ? 'w-full rounded-lg px-1.5 py-1 enabled:hover:bg-surface-3'
    : 'rounded-lg border border-border-subtle bg-surface-2 px-4 py-2 font-medium enabled:hover:border-border-strong enabled:hover:bg-surface-3'
})

/** Classes placing the panel above or below the trigger, per the measured {@link side}. */
const panelClasses: ComputedRef<string> = computed(() => {
  return side.value === 'above' ? 'bottom-full mb-1' : 'top-full mt-1'
})

/**
 * Decides which side the open panel is drawn on from the room the trigger
 * actually has. Measured rather than assumed: the same component is used in a
 * table row with the whole page below it and in a sidebar footer with nothing
 * below it at all.
 *
 * Downwards stays the preference — it is where a menu is expected — and the
 * panel flips up only when it does not fit below AND fits better above, so a
 * viewport too short for the menu either way keeps the familiar direction.
 * @returns Nothing; the side updates synchronously.
 */
const updateSide = (): void => {
  const trigger = triggerElement.value
  const panel = menuElement.value
  if (trigger === null || panel === null) {
    return
  }

  const triggerRect = trigger.getBoundingClientRect()
  const panelHeight = panel.getBoundingClientRect().height
  const roomBelow = window.innerHeight - triggerRect.bottom - VIEWPORT_MARGIN
  const roomAbove = triggerRect.top - VIEWPORT_MARGIN

  side.value = panelHeight > roomBelow && roomAbove > roomBelow ? 'above' : 'below'
}

/**
 * Collects the panel's enabled items in DOM order. Read from the DOM rather
 * than from a registry, because the items arrive through a slot: the dropdown
 * cannot know which of them a caller rendered with `v-if` at any moment.
 * @returns The focusable menu items, in the order the user meets them.
 */
const items = (): HTMLElement[] => {
  return Array.from(
    menuElement.value?.querySelectorAll<HTMLElement>(
      '[role="menuitem"]:not([disabled]),[role="menuitemradio"]:not([disabled])',
    ) ?? [],
  )
}

/**
 * Moves real focus to the item at a position, clamped to the ends of the list.
 * @param index Position to focus; values outside the list clamp to its ends.
 * @returns Nothing; focus moves synchronously.
 */
const focusItemAt = (index: number): void => {
  const menuItems = items()
  if (menuItems.length === 0) {
    return
  }
  const clamped = Math.min(Math.max(index, 0), menuItems.length - 1)
  menuItems[clamped]?.focus()
}

/**
 * Opens the panel and lands focus on one of its ends.
 * @param edge Which item to focus once the panel exists.
 * @returns Resolves after the panel has rendered and focus has moved — the
 * items do not exist until Vue has flushed the `v-if`.
 */
const open = async (edge: 'first' | 'last'): Promise<void> => {
  if (props.disabled) {
    return
  }
  isOpen.value = true
  await nextTick()
  // The panel has to exist before it can be measured, so the side is decided
  // after the flush rather than from an estimate of its height.
  updateSide()
  focusItemAt(edge === 'first' ? 0 : items().length - 1)
}

/**
 * Closes the panel and returns focus to the trigger.
 * @returns Nothing; state updates synchronously.
 */
const close = (): void => {
  isOpen.value = false
  triggerElement.value?.focus()
}

/**
 * Closes the panel without moving focus, for dismissals the user did not
 * initiate from the keyboard (an outside click), where stealing focus back
 * would fight what the user just did.
 * @returns Nothing; state updates synchronously.
 */
const dismiss = (): void => {
  isOpen.value = false
}

/**
 * Toggles the panel from the trigger's pointer click, landing focus on the first
 * item — the panel's only tab stop, so leaving focus on the trigger would strand
 * the next Tab outside a menu the user just opened.
 * @returns Resolves once an opening panel has rendered and focus has moved.
 */
const onTriggerClick = async (): Promise<void> => {
  if (isOpen.value) {
    dismiss()
    return
  }
  await open('first')
}

/**
 * Moves focus by one item within the open panel, stopping at the ends rather
 * than wrapping.
 * @param step Direction: 1 forwards, -1 backwards.
 * @returns Nothing; focus moves synchronously.
 */
const moveFocus = (step: number): void => {
  const menuItems = items()
  const current = menuItems.findIndex((item: HTMLElement): boolean => {
    return item === document.activeElement
  })
  focusItemAt(current + step)
}

/**
 * Closes the panel once an item has been chosen. Bound on the panel rather than
 * on each item, because the items are slotted content whose emits this
 * component cannot listen to.
 * @returns Nothing; state updates synchronously.
 */
const onMenuClick = (): void => {
  close()
}

/**
 * Closes the panel when focus leaves the component entirely (a Tab out of the
 * last item), which is the menu pattern's expected dismissal.
 * @param event The native focusout event.
 * @returns Nothing; state updates synchronously.
 */
const onFocusOut = (event: FocusEvent): void => {
  const next = event.relatedTarget
  if (next instanceof Node && rootElement.value?.contains(next) === true) {
    return
  }
  dismiss()
}

/**
 * Closes the panel on a click outside the component. Bound to `mousedown` so
 * the menu is gone before the outside target reacts.
 * @param event The document-level pointer event.
 * @returns Nothing; state updates synchronously.
 */
const onDocumentMouseDown = (event: MouseEvent): void => {
  if (!isOpen.value) {
    return
  }
  const target = event.target
  if (target instanceof Node && rootElement.value?.contains(target) === true) {
    return
  }
  dismiss()
}

/**
 * Re-decides the panel's side when the viewport changes under an open menu —
 * a rotated phone, an on-screen keyboard, a resized window — so a menu that fit
 * below when it opened does not stay there once it no longer does.
 * @returns Nothing; the side updates synchronously.
 */
const onViewportResize = (): void => {
  if (!isOpen.value) {
    return
  }
  updateSide()
}

onMounted((): void => {
  document.addEventListener('mousedown', onDocumentMouseDown)
  window.addEventListener('resize', onViewportResize)
})

onBeforeUnmount((): void => {
  document.removeEventListener('mousedown', onDocumentMouseDown)
  window.removeEventListener('resize', onViewportResize)
})
</script>

<template>
  <div
    ref="rootElement"
    class="relative"
    :class="variant === 'bare' ? 'min-w-0' : 'inline-block'"
    @focusout="onFocusOut"
  >
    <button
      :id="triggerId"
      ref="triggerElement"
      type="button"
      aria-haspopup="menu"
      :aria-label="ariaLabel"
      :aria-expanded="isOpen"
      :aria-controls="isOpen ? menuId : undefined"
      :disabled="disabled"
      class="inline-flex items-center gap-1.5 text-base text-text-primary transition-colors focus-visible:border-accent focus-visible:shadow-focus focus-visible:outline-none disabled:cursor-not-allowed disabled:text-text-muted disabled:opacity-65"
      :class="triggerClasses"
      @click="onTriggerClick"
      @keydown.enter.prevent="open('first')"
      @keydown.space.prevent="open('first')"
      @keydown.down.prevent="open('first')"
      @keydown.up.prevent="open('last')"
    >
      <!-- Optional leading mark, drawn by the caller so the kit stays free of any
           icon set. -->
      <slot name="leading"></slot>
      <!-- The trigger's visible content. A caller with more to show than a word
           — an avatar above a name and a role — renders it here instead, and
           `label` stays the trigger's plain-text name for the default case. -->
      <slot name="trigger">
        <span>{{ label }}</span>
      </slot>
      <UiIcon v-if="chevron" name="chevronDown" size="sm" class="text-text-muted" />
    </button>
    <ul
      v-if="isOpen"
      :id="menuId"
      ref="menuElement"
      role="menu"
      :aria-labelledby="triggerId"
      class="absolute z-10 min-w-48 rounded-lg border border-border-strong bg-surface-2 p-1.5 shadow-[0_12px_32px_rgb(0_0_0/0.4)]"
      :class="[align === 'end' ? 'right-0' : 'left-0', panelClasses]"
      @click="onMenuClick"
      @keydown.down.prevent="moveFocus(1)"
      @keydown.up.prevent="moveFocus(-1)"
      @keydown.home.prevent="focusItemAt(0)"
      @keydown.end.prevent="focusItemAt(items().length - 1)"
      @keydown.esc.prevent="close"
    >
      <slot />
    </ul>
  </div>
</template>
