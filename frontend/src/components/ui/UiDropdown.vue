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
 * The landing item is the FIRST one for a menu of commands and the CHOSEN one
 * for a menu that picks a value — see {@link landingIndex} for why a menu of
 * `menuitemradio` items that opened on its first item silently changed the
 * user's setting.
 *
 * The panel picks its own vertical side: it prefers to open downwards and flips
 * upwards when the space below the trigger cannot hold it. A menu in a sidebar
 * footer is the case that forced this — it sits against the bottom edge of the
 * viewport, so a menu that only ever opened downwards was unreachable there —
 * and the decision belongs here rather than at that one call site, because
 * every future footer menu has the same problem.
 *
 * The panel is rendered into `body` and positioned in viewport coordinates,
 * NOT next to the trigger in the document. A menu absolutely positioned inside
 * its trigger's box is clipped by any ancestor that scrolls, and the panel's
 * own tables are exactly that: `UiTable` scrolls horizontally inside its own
 * container so a wide table never moves the page sideways. A row's actions menu
 * opened there was cut off at the container's edge — the first command was half
 * visible and the second was not on screen at all. Escaping the ancestor is the
 * only fix that does not give up the table's own scrolling.
 *
 * The cost of leaving the document flow is that the panel no longer travels
 * with its trigger by itself, so a scroll that moves the trigger re-places the
 * panel against it and a scroll that carries the trigger off screen closes the
 * menu — see {@link onAnyScroll} for why closing on ANY move was not tenable.
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

/** Gap in CSS pixels between the trigger and the panel. */
const TRIGGER_GAP = 4

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

/**
 * Whether the open panel has been measured and placed against its trigger yet.
 *
 * The panel cannot be positioned until it exists, so for one flush after
 * `isOpen` turns true it is still carrying the coordinates of whatever the
 * previous open left behind — `{ top: 0, left: 0 }` on the first one, which
 * draws a fixed-position menu in the viewport's top-left corner. It is kept
 * `visibility: hidden` until {@link updateSide} has run, so that frame is not
 * painted anywhere and the panel appears only where it belongs.
 */
const isPlaced: Ref<boolean> = ref(false)

/** The component's outermost element, used to tell an outside click from an inside one. */
const rootElement: Ref<HTMLElement | null> = ref(null)

/** The trigger button, which focus returns to whenever the menu closes. */
const triggerElement: Ref<HTMLButtonElement | null> = ref(null)

/** The menu panel, queried for its items so slotted content needs no registration protocol. */
const menuElement: Ref<HTMLElement | null> = ref(null)

/**
 * The panel's viewport coordinates while it is open.
 *
 * Held as numbers rather than as classes because the panel is positioned
 * against the trigger's measured box, and no set of utility classes can express
 * "wherever that element happens to be right now".
 */
const panelOffset: Ref<{ top: number; left: number }> = ref({ top: 0, left: 0 })

/**
 * Which side the panel is drawn on. Starts below and is re-decided from real
 * measurements every time the panel opens; it is never read from a prop,
 * because only the browser knows how much room the trigger has at that moment.
 */
const side: Ref<UiDropdownSide> = ref('below')

/**
 * Where the trigger sat, in viewport coordinates, when the panel was last
 * placed against it. It is what tells a scroll that MOVED the trigger from one
 * that did not.
 *
 * The distinction is not academic. A pointer press focuses the trigger, and a
 * browser scrolls a focused control into view when it is not fully visible —
 * but it dispatches that scroll event asynchronously, a frame later, by which
 * time this component has already opened its panel and started listening. So
 * the scroll that BROUGHT the user to the trigger arrived after the panel
 * existed and dismissed it in the same breath: on a long screen the menu could
 * not be opened at all, and on a short one it opened fine, which is why this
 * survived review. Comparing the trigger's box answers the question the
 * dismissal is actually asking — has the panel been left floating beside a
 * control that has moved? — instead of trusting the arrival of an event whose
 * timing says nothing about that.
 *
 * That comparison is necessary and it was not sufficient, because the deferred
 * scroll can also MOVE the trigger — by a single pixel, when the trigger sits
 * just short of the scroll container's edge — and then the comparison reports
 * movement that the user never caused. What removes that case is not a larger
 * tolerance here but {@link onTriggerMouseDown}, which stops the browser
 * scrolling on focus at all; this comparison remains for every other scroll
 * event that arrives without having moved anything.
 */
const anchor: Ref<{ top: number; left: number }> = ref({ top: 0, left: 0 })

/** Classes positioning the trigger's box, which the `bare` variant does not draw. */
const triggerClasses: ComputedRef<string> = computed(() => {
  return props.variant === 'bare'
    ? 'w-full rounded-lg px-1.5 py-1 enabled:hover:bg-surface-3'
    : 'rounded-lg border border-border-subtle bg-surface-2 px-4 py-2 font-medium enabled:hover:border-border-strong enabled:hover:bg-surface-3'
})

/** Inline placement of the panel, in viewport coordinates. */
const panelStyle: ComputedRef<Record<string, string>> = computed(() => {
  return {
    top: `${panelOffset.value.top}px`,
    left: `${panelOffset.value.left}px`,
    visibility: isPlaced.value ? 'visible' : 'hidden',
  }
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
  const panelRect = panel.getBoundingClientRect()

  // Recorded wherever the panel is placed, so `onAnyScroll` compares against the
  // box this placement was actually derived from and not an older one.
  anchor.value = { top: triggerRect.top, left: triggerRect.left }

  const roomBelow = window.innerHeight - triggerRect.bottom - VIEWPORT_MARGIN
  const roomAbove = triggerRect.top - VIEWPORT_MARGIN

  side.value = panelRect.height > roomBelow && roomAbove > roomBelow ? 'above' : 'below'

  // `end` aligns the panel's right edge with the trigger's, which is what keeps
  // a menu in the last column of a table from opening off the right of the
  // viewport. Both edges are then clamped, because a trigger near an edge can
  // put a wide panel outside the viewport whichever way it is aligned.
  const preferredLeft =
    props.align === 'end' ? triggerRect.right - panelRect.width : triggerRect.left
  const furthestLeft = window.innerWidth - panelRect.width - VIEWPORT_MARGIN
  const left = Math.max(VIEWPORT_MARGIN, Math.min(preferredLeft, furthestLeft))

  const top =
    side.value === 'above'
      ? triggerRect.top - panelRect.height - TRIGGER_GAP
      : triggerRect.bottom + TRIGGER_GAP

  panelOffset.value = { top, left }
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
  // `preventScroll` because the browser scrolls an off-screen focus target into
  // view, and this component now closes on ANY scroll: focusing the first item
  // could scroll an ancestor and dismiss the menu in the same frame it opened.
  menuItems[clamped]?.focus({ preventScroll: true })
}

/**
 * Position the panel lands focus on when it opens forwards — the chosen item in
 * a menu that picks a value, the first item in a menu of commands.
 *
 * The distinction is not cosmetic. A menu of `menuitemradio` items that opens
 * on its first item hands the keyboard's most natural gesture — open, then
 * confirm — the power to change a setting the user never asked to change: from
 * a Russian interface, trigger then Enter then Enter selected `English`,
 * because English is first in the language menu and the second Enter (a
 * deliberate press, or one held key's repeat) activated whatever was focused.
 * That was measured, not theorised. Opening on the chosen item makes the same
 * gesture re-confirm the value already in force, which is a no-op.
 *
 * Which item is chosen is read from the items' own `aria-checked` in the DOM,
 * not from a prop naming it and not from a roving `tabindex`:
 *
 * - The items arrive through a slot, so this component has no list to name an
 *   index into — the same reason {@link items} queries the DOM rather than
 *   keeping a registry. A prop would also duplicate a fact the caller has
 *   already stated on the item that owns it, and two statements of one fact
 *   drift: the tick, the announcement and the landing place would be free to
 *   disagree.
 * - `aria-checked` is not an approximation of the answer, it IS the answer —
 *   it is what a screen reader announces as the current value, so a menu that
 *   lands somewhere else is a menu that contradicts itself out loud.
 * - A roving `tabindex` tracks the item last FOCUSED, not the item selected.
 *   Those differ the moment a user arrows through a menu and dismisses it with
 *   Escape, and landing on what the user last looked at rather than on what is
 *   in force is a different defect wearing the same clothes.
 *
 * Two cases fall back to the first item, and both are deliberate. When nothing
 * is chosen there is no value to preserve and no accidental change to make, so
 * the command-menu answer is right. When the chosen item is DISABLED it is
 * absent from {@link items} and cannot take focus at all, so landing is forced
 * elsewhere; the first enabled item is the least surprising place, and the
 * gesture that follows can no longer re-select the disabled value — it selects
 * a visible neighbour, which the user can see they are on.
 * @returns Index into {@link items} to focus, always within the list when it
 * has any members.
 */
const landingIndex = (): number => {
  // `items()` already excludes disabled items, so a chosen-but-disabled item
  // reports -1 here and falls back with everything else that has no choice.
  const chosen = items().findIndex((item: HTMLElement): boolean => {
    return item.getAttribute('aria-checked') === 'true'
  })
  return chosen === -1 ? 0 : chosen
}

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
  isPlaced.value = false
  // Bound here rather than on mount: a table of twenty rows is twenty triggers,
  // and twenty listeners on every scroll frame is work done for a panel that is
  // not on screen.
  window.addEventListener('scroll', onAnyScroll, true)
  await nextTick()
  // The panel has to exist before it can be measured, so the side is decided
  // after the flush rather than from an estimate of its height.
  updateSide()
  isPlaced.value = true
  // Focus cannot land on a `visibility: hidden` element, so the reveal above has
  // to reach the DOM before the item is asked to take it.
  await nextTick()
  focusItemAt(edge === 'first' ? landingIndex() : items().length - 1)
}

/**
 * Whether a node belongs to this dropdown — its trigger or its panel.
 *
 * The panel is teleported into `body`, so it is NOT a descendant of the
 * component's root element. Asking the root alone, as this used to, would
 * report every click and every focus inside the open menu as "outside" and
 * dismiss the menu the moment it was used.
 * @param node The node to test.
 * @returns True when the node is inside the trigger's box or the panel.
 */
const containsNode = (node: Node): boolean => {
  return rootElement.value?.contains(node) === true || menuElement.value?.contains(node) === true
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
 * Measured on the databases and SFTP rows, where every destructive command is
 * chosen from this menu.
 *
 * Focus on `<body>` still counts as "nobody has it": that is where the browser
 * leaves it when the focused item is removed, and it is exactly the case the
 * trigger has to be given it back.
 * @returns Nothing; state updates synchronously.
 */
const close = (): void => {
  isOpen.value = false
  window.removeEventListener('scroll', onAnyScroll, true)

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
  window.removeEventListener('scroll', onAnyScroll, true)
}

/**
 * Takes focus for the trigger on a pointer press, in place of the browser's own
 * default, and takes it WITHOUT scrolling.
 *
 * The default is not merely redundant here, it is a defect: a browser scrolls a
 * newly focused control into view when it is not fully visible, and it applies
 * that adjustment asynchronously, a frame or so after the press. The panel,
 * meanwhile, is placed against the trigger's box in the same turn as the click.
 * When the adjustment lands afterwards the trigger has moved out from under a
 * panel that has already been positioned, and {@link onAnyScroll} — correctly,
 * on the information it has — dismisses the menu the user has just opened.
 *
 * Measured, not theorised: a row-actions menu in the last row of the firewall
 * screen's whitelist sits one pixel short of the shell's scroll container, and
 * the container scrolled from 1278 to 1279 after the press. Whether that pixel
 * arrived before or after the placement decided whether the menu stayed open,
 * which under load made an end-to-end spec fail roughly one run in five.
 * Suppressing the default and focusing with `preventScroll` removes the race
 * rather than widening a tolerance around it: no scroll is provoked at all, so
 * `onAnyScroll` keeps its one meaning — the user scrolled.
 *
 * Focus still has to be taken explicitly, because `preventDefault` on
 * `mousedown` is what stops the browser giving it, and the menu pattern needs
 * the trigger focused so that closing can return focus to it.
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
 * Puts focus back on the trigger BEFORE the chosen command runs, in the capture
 * phase of the same click that will close the panel on its way back out.
 *
 * Without it, a command that opens a dialog opens it from a doomed element.
 * Chromium takes a microtask checkpoint between the listeners of one click, so
 * the item's own handler settles Vue's queue — the dialog mounts, remembers
 * whatever has focus so it can restore it, and takes focus for itself — while
 * the item still holds focus and is about to be unmounted with the panel. The
 * dialog was then remembering a detached node, and closing it dropped the
 * keyboard user at the top of the document. Measured on the databases and SFTP
 * rows, where every destructive command is chosen from this menu.
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

/**
 * Closes the panel when focus leaves the component entirely (a Tab out of the
 * last item), which is the menu pattern's expected dismissal.
 * @param event The native focusout event.
 * @returns Nothing; state updates synchronously.
 */
const onFocusOut = (event: FocusEvent): void => {
  const next = event.relatedTarget
  if (next instanceof Node && containsNode(next)) {
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
  if (target instanceof Node && containsNode(target)) {
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

/**
 * Keeps an open panel with its trigger when a scroll has MOVED that trigger, and
 * closes the panel once the trigger has been carried off screen.
 *
 * A panel positioned in viewport coordinates does not travel with its trigger,
 * so a scroll leaves the menu floating beside a control that is no longer
 * there. This component used to close on any such move. That answer looked
 * honest and was not, because "the trigger moved" does not mean "the user
 * scrolled": the browser itself scrolls to reveal things, and a menu opened on
 * the last row of a scroll container was measured being dismissed by a ONE
 * PIXEL adjustment nobody asked for, one press in five under load (see
 * {@link onTriggerMouseDown}). No tolerance around the comparison can separate
 * those two cases — a pixel of user scroll and a pixel of browser scroll are
 * the same pixel — so the panel follows its anchor instead, which is what a
 * menu bound to a row should do anyway. The cost is one re-placement per scroll
 * frame for the single panel that is open, which is two rect reads.
 *
 * It still closes when the trigger has left the viewport entirely: a menu for a
 * row that is no longer on screen belongs to nothing, and following it would
 * park the panel against an edge.
 *
 * What is measured is the TRIGGER's box, because the question is whether the
 * placement recorded in {@link anchor} still describes where the trigger is. A
 * scroll event that arrives without having moved it costs one rect read and
 * nothing else: see {@link anchor} for the pointer-press case that made an
 * unconditional close dismiss menus the user had only just opened.
 *
 * Bound in the capture phase so it also sees scrolling inside a container —
 * a scroll event on an element does not bubble to the document.
 * @returns Nothing; state updates synchronously.
 */
const onAnyScroll = (): void => {
  if (!isOpen.value) {
    return
  }

  const trigger = triggerElement.value
  if (trigger === null) {
    dismiss()
    return
  }

  const rect = trigger.getBoundingClientRect()
  // Sub-pixel tolerance: a fractional layout position must not read as movement,
  // and re-placing on every frame of a smooth scroll that changed nothing is
  // work for no one.
  if (Math.abs(rect.top - anchor.value.top) <= 0.5 && Math.abs(rect.left - anchor.value.left) <= 0.5) {
    return
  }

  const isOnScreen =
    rect.bottom > 0 && rect.top < window.innerHeight && rect.right > 0 && rect.left < window.innerWidth
  if (isOnScreen) {
    updateSide()
    return
  }
  dismiss()
}

onMounted((): void => {
  document.addEventListener('mousedown', onDocumentMouseDown)
  window.addEventListener('resize', onViewportResize)
})

onBeforeUnmount((): void => {
  document.removeEventListener('mousedown', onDocumentMouseDown)
  window.removeEventListener('resize', onViewportResize)
  // Also here, because a component unmounted while its panel is open — a row
  // removed from the table under an open menu — never reaches `close`.
  window.removeEventListener('scroll', onAnyScroll, true)
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
      <ul
        v-if="isOpen"
        :id="menuId"
        ref="menuElement"
        role="menu"
        :aria-labelledby="triggerId"
        class="fixed z-50 min-w-48 rounded-lg border border-border-strong bg-surface-2 p-1.5 shadow-[0_12px_32px_rgb(0_0_0/0.4)]"
        :style="panelStyle"
        @click="onMenuClick"
        @click.capture="onMenuClickCapture"
        @focusout="onFocusOut"
        @keydown.down.prevent="moveFocus(1)"
        @keydown.up.prevent="moveFocus(-1)"
        @keydown.home.prevent="focusItemAt(0)"
        @keydown.end.prevent="focusItemAt(items().length - 1)"
        @keydown.esc.prevent="close"
      >
        <slot />
      </ul>
    </Teleport>
  </div>
</template>
