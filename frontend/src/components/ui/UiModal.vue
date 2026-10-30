<script setup lang="ts">
/**
 * Modal dialog: a titled panel over a backdrop that takes the whole
 * interaction until it is dismissed. Used for a focused sub-task (create a
 * record) and for confirming a destructive action, which rules/vue.md
 * requires to be confirmed rather than performed on a single click.
 *
 * It carries the full dialog contract so screens do not have to:
 * `role="dialog"` with `aria-modal`, an accessible name taken from the
 * rendered title, a focus trap that cycles Tab and Shift+Tab inside the
 * panel, Escape and backdrop-click dismissal (both suppressed together by
 * `:dismissible="false"` for a dialog whose content cannot be recovered), and
 * focus restored to whatever element opened it (or, when that element cannot
 * take focus, to `returnFocusTo`, then to `<main>`). The dialog is teleported to
 * `<body>` so no ancestor's `overflow` or stacking context can clip it.
 *
 * The caller owns the open state: this component reports `close` and never
 * closes itself behind the caller's back.
 *
 * **Look.** A `rounded-panel` `bg-surface-1` sheet with a shadow, over the same
 * darkened and blurred backdrop the shell's drawer uses (`bg-page` at 70% plus a
 * 4px blur), so every overlay in the panel dims the page the same way. The title
 * is set in the display face but NOT uppercased: a dialog title routinely
 * contains an identifier ("Delete site shop.example.com"), and capitals would
 * change what that identifier says. Contrast, measured (dark / light theme):
 * title (`text-text-primary`) on the sheet is 15.70 / 17.98:1, body
 * (`text-text-secondary`) 7.08 / 6.00:1, the close glyph (`text-text-muted`)
 * 5.36 / 5.41:1 and 4.62 / 4.57:1 once its `bg-surface-3` hover is under it.
 * The close control is 32px square, over the 24px minimum target.
 *
 * **Motion.** The backdrop fades and the sheet fades up from 95% scale: 300ms in, 200ms out, so
 * leaving is quicker than arriving (a dismissed dialog should get out of the way). The leave runs
 * inside a `<Transition>`, so the caller still just flips `open`. Under `prefers-reduced-motion`
 * both directions are instant.
 */
import { ref, useId, type Ref } from 'vue'
import { useFocusTrap, type FocusTarget } from '../../composables/useFocusTrap'
import UiIcon from './UiIcon.vue'

/** Props accepted by {@link UiModal}. */
const props = withDefaults(
  defineProps<{
    /** Whether the dialog is currently shown; owned by the caller. */
    open: boolean
    /** Dialog heading, already translated by the caller; also its accessible name. */
    title: string
    /** Accessible name for the close button, already translated by the caller. */
    closeLabel: string
    /**
     * Whether the user may dismiss the dialog themselves — by clicking the
     * backdrop or pressing Escape. Turn it OFF for a dialog whose content
     * cannot be recovered once it is gone: the credential dialogs show a
     * password that exists nowhere else in the product, so a mis-aimed click
     * beside the panel or a reflexive Escape would destroy it with no way
     * back. Such a dialog still closes through its own explicit control and
     * through the close button in the header.
     *
     * Both gestures are governed by this ONE prop rather than two, because a
     * dialog that refuses a backdrop click while still answering Escape is
     * protected against the accident nobody makes and open to the one they do.
     */
    dismissible?: boolean
    /**
     * Where focus goes on close when the element that opened the dialog cannot take it back —
     * `<body>`, removed, hidden or disabled by then. An element, or a getter evaluated at close
     * time (so it can name a control that is only re-enabled once the dialog's work is done).
     *
     * The case is real: a credential dialog opens from a form's submit button, which is disabled
     * while the request runs, so the browser reported `<body>` as the opener and closing the
     * dialog dropped the keyboard user at the top of the document. Left unset, focus falls back to
     * the `<main>` landmark — inside the content, never `<body>` — which is right for any dialog
     * whose opener is gone; set it only when a more specific control is the natural next stop.
     */
    returnFocusTo?: FocusTarget
    /**
     * The element to focus when the dialog opens, as a getter evaluated after the panel renders.
     * Left unset, focus goes to the first focusable element (the header's close control). A
     * confirmation passes its main action so Enter answers the question it is asking.
     */
    initialFocus?: () => HTMLElement | null | undefined
  }>(),
  { dismissible: true, returnFocusTo: undefined, initialFocus: undefined },
)

/** Events emitted by {@link UiModal}. */
const emit = defineEmits<{
  /** Fired when the user dismisses the dialog (close button, Escape, or backdrop). */
  (e: 'close'): void
}>()

/** Stable, unique id linking the panel to its heading via `aria-labelledby`. */
const titleId: string = useId()

/** The dialog panel, whose focusable descendants the trap cycles through. */
const panelElement: Ref<HTMLElement | null> = ref(null)

/**
 * Reports a dismissal. The caller decides whether the dialog actually closes.
 * @returns Nothing; emits synchronously.
 */
const requestClose = (): void => {
  emit('close')
}

/**
 * Dismisses on a backdrop click, unless the caller has declared the dialog
 * undismissable. Clicks that land inside the panel and bubble out are ignored.
 * @param event The native mouse event on the backdrop.
 * @returns Nothing; emits synchronously.
 */
const onBackdropClick = (event: MouseEvent): void => {
  if (!props.dismissible) {
    return
  }
  // Only a click whose target IS the backdrop dismisses. Asking "is the target inside the panel"
  // instead fails for a control that removes the clicked node before the click bubbles here — a
  // select's option is gone once chosen, so the detached node was read as outside and the dialog
  // closed under the operator (#216).
  if (event.target !== event.currentTarget) {
    return
  }
  requestClose()
}

/**
 * Dismisses on Escape, unless the caller has declared the dialog undismissable.
 *
 * Escape is guarded for the same reason the backdrop is, and it is the likelier
 * of the two accidents: it is one keystroke, it is muscle memory, and on a
 * dialog showing a value that exists nowhere else it destroys that value with
 * nothing to undo it.
 * @returns Nothing; emits synchronously when dismissal is allowed.
 */
const onEscape = (): void => {
  if (!props.dismissible) {
    return
  }
  requestClose()
}

// Focus management is the shared trap (composables/useFocusTrap.ts): focus enters when the dialog
// opens, Tab wraps inside the panel, and focus returns on close — to the opener when it can still
// take focus, else to `returnFocusTo`, else to `<main>`. It runs `immediate`, which is the whole of
// the contract for a caller that MOUNTS this component already open (`v-if="x"` beside `:open="x"`,
// what a dialog whose props are only valid while it is open has to write): without it focus stays
// on the page behind and Escape, a `keydown` on the backdrop, never fires. Both destructive dialogs
// in the product (account deletion, backup restore) were in exactly that state.
const { onTab } = useFocusTrap({
  panel: panelElement,
  open: (): boolean => {
    return props.open
  },
  returnFocusTo: (): FocusTarget => {
    return props.returnFocusTo
  },
  initialFocus: (): HTMLElement | null | undefined => {
    return props.initialFocus?.()
  },
})
</script>

<template>
  <Teleport to="body">
    <Transition name="ui-modal">
      <!-- `mousedown.self.prevent`: a press on the backdrop must not blur the panel.
           Focus on <body> is outside the trap, and the Escape and Tab handlers below
           never fire for it. Only the backdrop itself is suppressed, so presses
           inside the panel still focus and select normally. -->
      <!-- z-[60] is above UiDropdown's panel, which is also teleported to body and
           sits at z-50. Two teleported siblings at the SAME z resolve by mount
           order, which is not a rule anyone can read off the page. -->
      <div
        v-if="open"
        class="fixed inset-0 z-[60] flex items-center justify-center bg-page/70 p-3 backdrop-blur-sm sm:p-5"
        @mousedown.self.prevent
        @click="onBackdropClick"
        @keydown.esc.prevent="onEscape"
        @keydown.tab="onTab"
      >
        <div
          ref="panelElement"
          role="dialog"
          aria-modal="true"
          :aria-labelledby="titleId"
          tabindex="-1"
          class="ui-modal-panel flex max-h-full w-full max-w-md flex-col overflow-hidden rounded-panel border border-border-strong bg-surface-1 shadow-2xl focus-visible:outline-none"
        >
          <div class="flex shrink-0 items-start justify-between gap-3 px-5 pt-4 pb-2">
            <!-- `overflow-wrap: anywhere`: a long Armenian title or an unbroken
                 domain must wrap inside the sheet, never push it wider than a
                 390px screen. -->
            <h2
              :id="titleId"
              class="min-w-0 font-display text-base font-extrabold text-text-primary wrap-anywhere"
            >
              {{ title }}
            </h2>
            <button
              type="button"
              class="inline-flex size-11 shrink-0 sm:size-7 items-center justify-center rounded-inset text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none"
              @click="requestClose"
            >
              <!-- Decorative glyph: the button's accessible name comes from the caller-translated label. -->
              <UiIcon name="x" size="sm" />
              <span class="sr-only">{{ closeLabel }}</span>
            </button>
          </div>
          <!-- The body scrolls, the header and the footer do not. A dialog taller than the
               viewport otherwise clips its own footer: the panel is centred and hidden past its
               edges, so the confirm button sits off-screen with nothing to scroll it into view —
               which turns "the user must read this before acting" into "the user cannot act". Found
               on the backup restore dialog, whose body states what a restore replaces and what it
               does not, and is long because it has to be.
               `overflow-y: auto` also clips on the other axis and at the padding edge, and the kit's
               focus ring is a 4px shadow OUTSIDE the control: a last input flush against the end of
               the scrolled content lost the bottom of its ring (backup restore's account-name field).
               So the body keeps 4px above (`pt-1`, taken from the header's bottom padding so nothing
               moves) and 24px below, and `scroll-pb-6` makes keyboard focus scroll the ring into view
               rather than the control's bare edge. -->
          <div
            class="min-h-0 flex-1 scroll-pb-5 overflow-y-auto px-5 pt-1 pb-5 text-sm leading-relaxed text-text-secondary wrap-anywhere"
          >
            <slot />
          </div>
          <!-- The actions sit under a rule on the SAME surface as the body: a raised
               footer would put a `bg-surface-2` secondary button on a `bg-surface-2`
               strip, leaving only its hairline border to show where it is. They wrap
               rather than overflow, because translated labels differ in length by
               half again between the three languages. -->
          <div
            v-if="$slots.footer"
            class="flex shrink-0 flex-wrap justify-end gap-2 border-t border-border-subtle px-5 py-3"
          >
            <slot name="footer" />
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
/* Enter and leave are a `<Transition>` rather than a keyframe so the dialog can animate OUT
   as well: a keyframe only runs on mount. The backdrop fades, the sheet fades and scales. */
.ui-modal-enter-active,
.ui-modal-enter-active .ui-modal-panel {
  transition:
    opacity 300ms ease-out,
    transform 300ms ease-out;
}

.ui-modal-leave-active,
.ui-modal-leave-active .ui-modal-panel {
  transition:
    opacity 200ms ease-in,
    transform 200ms ease-in;
}

.ui-modal-enter-from,
.ui-modal-leave-to {
  opacity: 0;
}

.ui-modal-enter-from .ui-modal-panel,
.ui-modal-leave-to .ui-modal-panel {
  opacity: 0;
  transform: scale(0.95);
}

/* For a reader who asked for less motion the dialog simply appears and disappears. */
@media (prefers-reduced-motion: reduce) {
  .ui-modal-enter-active,
  .ui-modal-enter-active .ui-modal-panel,
  .ui-modal-leave-active,
  .ui-modal-leave-active .ui-modal-panel {
    transition: none;
  }
}
</style>
