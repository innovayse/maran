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
 * and Arrow Down open on the menu's landing item, Arrow Up on the last, Home
 * and End jump to the ends, Escape closes, and focus returns to the trigger on
 * every close so a keyboard user is never dropped at the top of the document.
 *
 * Items are supplied through the default slot as `UiDropdownItem` components;
 * choosing one closes the menu.
 *
 * **Look.** The panel floats, so it is the one place a shadow is right: a
 * `bg-surface-1` `rounded-control` panel with a strong border and `shadow-xl`,
 * its rows (`UiDropdownItem`) concentric inside its 6px padding. The boxed
 * trigger is a `bg-surface-2` control with a visible label, so it is never
 * identified by its border alone; primary text on it is 14.75 / 16.31:1 and
 * the `bg-surface-3` hover is 13.55 / 15.17:1 (dark / light). The `bare`
 * trigger only gains the `bg-surface-3` hover, over content that already
 * reads as a control. Every trigger is at least 40px tall in its boxed form.
 *
 * The panel picks its own vertical side (downwards when it fits, upwards when it does not) and is
 * rendered into `body`, positioned in viewport coordinates, so that no scrolling ancestor — a
 * table's own container — can clip it. Both decisions, and how the panel follows a trigger that
 * moves, live in {@link useDropdownPlacement}; the keyboard navigation and the landing item (the
 * FIRST one for a menu of commands, the CHOSEN one for a menu that picks a value) in
 * {@link useDropdownItems}; dismissal by an outside press in {@link useDropdownDismissal}.
 *
 * **Motion.** The panel fades in while sliding 4px into place (150ms) and fades
 * out faster (100ms) without moving, so a closed menu never lingers over the
 * next thing the user clicks. Under `prefers-reduced-motion` it appears and
 * disappears at once.
 */
import { computed, nextTick, ref, useId, type ComputedRef, type Ref } from 'vue'
import { useDropdownDismissal } from '../../composables/useDropdownDismissal'
import { useDropdownItems } from '../../composables/useDropdownItems'
import { useDropdownPlacement } from '../../composables/useDropdownPlacement'
import { dropdownTriggerClasses } from '../../utils/dropdownTriggerClasses'
import type { ButtonSize, ButtonVariant } from './UiButton.vue'
import UiIcon from './UiIcon.vue'

/**
 * How the trigger is drawn. `button` is the kit's boxed control; `bare` draws
 * no box of its own, for a trigger whose slotted content already reads as a
 * control (the sidebar's identity row).
 */
export type UiDropdownVariant = 'button' | 'bare'

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
    /**
     * Draws the trigger as a `UiButton` of this variant instead of the dropdown's own boxed or
     * bare trigger, for a menu that belongs in a toolbar next to other buttons (the language
     * picker). It replaces `variant`'s box entirely; the ARIA wiring and the keyboard handling are
     * the same either way. Leave it unset for the default trigger.
     */
    triggerVariant?: ButtonVariant
    /** Geometry of the trigger when `triggerVariant` is set (see `ButtonSize`); ignored otherwise. */
    triggerSize?: ButtonSize
  }>(),
  {
    align: 'start',
    disabled: false,
    ariaLabel: undefined,
    chevron: true,
    variant: 'button',
    triggerVariant: undefined,
    triggerSize: 'md',
  },
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

/** Keyboard navigation over the panel's items. */
const { items, focusItemAt, landingIndex, moveFocus } = useDropdownItems(menuElement)

/** Where the panel is drawn and how it follows its trigger. */
const { panelStyle, hide, reveal, place, followScroll, stopFollowingScroll } = useDropdownPlacement({
  trigger: triggerElement,
  panel: menuElement,
  isOpen,
  align: (): 'start' | 'end' => {
    return props.align
  },
  onLost: (): void => {
    dismiss()
  },
})

/** Dismissal by a press or focus landing outside the dropdown. */
const { containsNode, onFocusOut } = useDropdownDismissal({
  isOpen,
  root: rootElement,
  menu: menuElement,
  dismiss: (): void => {
    dismiss()
  },
})

/** Classes of the trigger button; see {@link dropdownTriggerClasses}. */
const triggerClasses: ComputedRef<string> = computed(() => {
  return dropdownTriggerClasses(props.triggerSize, props.variant === 'bare', props.triggerVariant)
})

/**
 * Opens the panel and lands focus inside it.
 * @param edge `first` lands on {@link landingIndex} — the chosen item, or the
 * first when nothing is chosen; `last` lands on the final item, which is what
 * Arrow Up explicitly asks for.
 * @returns Resolves after the panel has rendered and focus has moved — the
 * items do not exist until Vue has flushed the `v-if`.
 */
const open = async (edge: 'first' | 'last'): Promise<void> => {
  if (props.disabled) {
    return
  }
  isOpen.value = true
  hide()
  followScroll()
  await nextTick()
  // The panel has to exist before it can be measured, so the side is decided
  // after the flush rather than from an estimate of its height.
  place()
  reveal()
  // Focus cannot land on a `visibility: hidden` element, so the reveal above has
  // to reach the DOM before the item is asked to take it.
  await nextTick()
  focusItemAt(edge === 'first' ? landingIndex() : items().length - 1)
}

/**
 * Closes the panel and returns focus to the trigger — unless something else has
 * already taken it.
 *
 * The guard is not defensive tidiness; without it a command that opens a dialog
 * loses that dialog its focus. Chromium runs a microtask checkpoint BETWEEN the
 * listeners of one bubbling click, so a chosen item's own handler settles Vue's
 * queue — the dialog mounts and focuses itself — before this handler, bound on
 * the panel, runs at all. An unconditional `focus()` here then pulls the user
 * back to a trigger behind a modal they cannot Tab into or Escape out of.
 *
 * Focus on `<body>` still counts as "nobody has it": that is where the browser
 * leaves it when the focused item is removed, and it is exactly the case the
 * trigger has to be given it back.
 * @returns Nothing; state updates synchronously.
 */
const close = (): void => {
  isOpen.value = false
  stopFollowingScroll()

  const active = document.activeElement
  const takenElsewhere = active instanceof Node && active !== document.body && !containsNode(active)
  if (takenElsewhere) {
    return
  }

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
  stopFollowingScroll()
}

/**
 * Leaves the menu on Tab or Shift+Tab: closes it and lets the browser continue from the TRIGGER.
 *
 * The panel is teleported to the end of `<body>`, so the browser's own idea of "the next tabbable
 * after the focused menu item" is the end of the document, and from there Tab wraps to the top —
 * the skip link. WAI-ARIA's menu button pattern wants the opposite: Tab closes the menu and moves
 * on from the button the user opened it with.
 *
 * The key is deliberately NOT cancelled. Focusing the trigger inside the keydown handler moves the
 * point the browser's default Tab action starts from, so that default then lands on the element
 * after the trigger (before it, for Shift+Tab) in the real document order. Doing the move by hand
 * would mean re-implementing "what is tabbable here" — hidden, inert, `tabindex`, shadow DOM — and
 * getting it subtly wrong.
 * @returns Nothing; the panel closes and focus moves synchronously.
 */
const onMenuTab = (): void => {
  close()
}

/**
 * Takes focus for the trigger on a pointer press, in place of the browser's own default, and takes
 * it WITHOUT scrolling.
 *
 * The default is a defect here: a browser scrolls a newly focused control into view, and applies
 * that adjustment a frame after the press, when the panel has already been placed against the
 * trigger's box. The trigger then moves out from under the panel and the scroll handler of
 * {@link useDropdownPlacement} dismisses the menu the user has just opened (measured: one press in
 * five under load, on a row one pixel short of the scroll container). Suppressing the default and
 * focusing with `preventScroll` removes the race instead of widening a tolerance around it. Focus
 * still has to be taken explicitly, because `preventDefault` on `mousedown` is what stops the
 * browser giving it, and closing must be able to return focus to the trigger.
 * @returns Nothing; focus moves synchronously.
 */
const onTriggerMouseDown = (): void => {
  triggerElement.value?.focus({ preventScroll: true })
}

/**
 * Toggles the panel from the trigger's pointer click, landing focus inside it —
 * the panel is the only tab stop the menu has, so leaving focus on the trigger
 * would strand the next Tab outside a menu the user just opened.
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
 * Closes the panel once an item has been chosen. Bound on the panel rather than
 * on each item, because the items are slotted content whose emits this
 * component cannot listen to.
 * @returns Nothing; state updates synchronously.
 */
const onMenuClick = (): void => {
  close()
}

/**
 * Puts focus back on the trigger BEFORE the chosen command runs, in the capture
 * phase of the same click that will close the panel on its way back out.
 *
 * Without it, a command that opens a dialog opens it from a doomed element.
 * Chromium takes a microtask checkpoint between the listeners of one click, so
 * the item's own handler settles Vue's queue — the dialog mounts, remembers
 * whatever has focus so it can restore it, and takes focus for itself — while
 * the item still holds focus and is about to be unmounted with the panel. The
 * dialog was then remembering a detached node, and closing it dropped the
 * keyboard user at the top of the document.
 *
 * Only focus moves here, not the close: unmounting the panel in the capture
 * phase would take the item's own click listener down with it, and the command
 * would never run at all — measured, as a menu whose commands did nothing.
 * @returns Nothing; focus moves synchronously.
 */
const onMenuClickCapture = (): void => {
  // `preventScroll` for the same reason the trigger's pointer press uses it: the
  // browser scrolls a newly focused control into view, and this component
  // re-places its panel on every scroll.
  triggerElement.value?.focus({ preventScroll: true })
}
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
      :class="triggerClasses"
      @mousedown.prevent="onTriggerMouseDown"
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
    <!-- Teleported so no scrolling ancestor can clip it; see this component's
         header. `fixed` because the coordinates are the viewport's. -->
    <Teleport to="body">
      <Transition name="ui-dropdown">
        <ul
          v-if="isOpen"
          :id="menuId"
          ref="menuElement"
          role="menu"
          :aria-labelledby="triggerId"
          class="fixed z-50 min-w-44 rounded-control border border-border-strong bg-surface-1 p-menu shadow-xl"
          :style="panelStyle"
          @click="onMenuClick"
          @click.capture="onMenuClickCapture"
          @focusout="onFocusOut"
          @keydown.down.prevent="moveFocus(1)"
          @keydown.up.prevent="moveFocus(-1)"
          @keydown.home.prevent="focusItemAt(0)"
          @keydown.end.prevent="focusItemAt(items().length - 1)"
          @keydown.esc.prevent="close"
          @keydown.tab="onMenuTab"
        >
          <slot />
        </ul>
      </Transition>
    </Teleport>
  </div>
</template>

<style scoped>
/* A Transition, not a keyframe, so the panel can animate out as well as in. */
.ui-dropdown-enter-active {
  transition:
    opacity 150ms ease-out,
    transform 150ms ease-out;
}

.ui-dropdown-leave-active {
  transition: opacity 100ms ease-in;
}

.ui-dropdown-enter-from {
  opacity: 0;
  transform: translateY(-4px);
}

.ui-dropdown-leave-to {
  opacity: 0;
}

@media (prefers-reduced-motion: reduce) {
  .ui-dropdown-enter-active,
  .ui-dropdown-leave-active {
    transition: none;
  }
}
</style>
