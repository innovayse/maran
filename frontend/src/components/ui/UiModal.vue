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
 * panel, Escape and backdrop-click dismissal, and focus restored to whatever
 * element opened it. The dialog is teleported to `<body>` so no ancestor's
 * `overflow` or stacking context can clip it.
 *
 * The caller owns the open state: this component reports `close` and never
 * closes itself behind the caller's back.
 */
import { nextTick, onBeforeUnmount, ref, watch, useId, type Ref } from 'vue'
import { focusableElements as focusableIn } from '../../utils/focusableElements'

/** Props accepted by {@link UiModal}. */
const props = withDefaults(
  defineProps<{
    /** Whether the dialog is currently shown; owned by the caller. */
    open: boolean
    /** Dialog heading, already translated by the caller; also its accessible name. */
    title: string
    /** Accessible name for the close button, already translated by the caller. */
    closeLabel: string
    /** Whether a click on the backdrop dismisses the dialog; turn it off for a step the user must answer. */
    dismissOnBackdrop?: boolean
  }>(),
  { dismissOnBackdrop: true },
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
 * The element that had focus when the dialog opened, so it can be focused again
 * on close. Without this the keyboard user would restart from the top of the
 * document after every dialog.
 */
const previouslyFocused: Ref<HTMLElement | null> = ref(null)

/**
 * Reports a dismissal. The caller decides whether the dialog actually closes.
 * @returns Nothing; emits synchronously.
 */
const requestClose = (): void => {
  emit('close')
}

/**
 * Dismisses on a backdrop click, ignoring clicks that started inside the panel
 * and bubbled out.
 * @param event The native mouse event on the backdrop.
 * @returns Nothing; emits synchronously.
 */
const onBackdropClick = (event: MouseEvent): void => {
  if (!props.dismissOnBackdrop) {
    return
  }
  const target = event.target
  if (target instanceof Node && panelElement.value?.contains(target) === true) {
    return
  }
  requestClose()
}

/**
 * Keeps Tab inside the dialog by wrapping at both ends — the focus trap. A modal
 * that lets Tab escape into the page behind it is modal for the mouse only.
 * @param event The keyboard event for Tab or Shift+Tab.
 * @returns Nothing; focus moves synchronously.
 */
const onTab = (event: KeyboardEvent): void => {
  const elements = focusableIn(panelElement.value)
  if (elements.length === 0) {
    // Nothing focusable inside: keep focus on the panel rather than letting Tab
    // leave the dialog for the inert page behind it.
    event.preventDefault()
    return
  }
  const first = elements[0]
  const last = elements[elements.length - 1]
  const active = document.activeElement
  // A backdrop click leaves focus on <body>, outside the panel; without this the
  // next Tab would walk into the page behind the dialog instead of into it.
  if (!(active instanceof Node) || panelElement.value?.contains(active) !== true) {
    event.preventDefault()
    first?.focus()
    return
  }
  if (event.shiftKey && (active === first || active === panelElement.value)) {
    event.preventDefault()
    last?.focus()
    return
  }
  if (!event.shiftKey && active === last) {
    event.preventDefault()
    first?.focus()
  }
}

// Focus enters the dialog when it opens and returns to the opener when it
// closes. Both directions are handled here so every caller gets the behaviour
// without writing any of it.
watch(
  (): boolean => {
    return props.open
  },
  async (isOpen: boolean): Promise<void> => {
    if (isOpen) {
      previouslyFocused.value = document.activeElement instanceof HTMLElement ? document.activeElement : null
      await nextTick()
      const elements = focusableIn(panelElement.value)
      // Fall back to the panel itself (tabindex="-1") when the dialog has no
      // focusable content yet, so focus is never left behind on the page.
      ;(elements[0] ?? panelElement.value)?.focus()
      return
    }
    previouslyFocused.value?.focus()
    previouslyFocused.value = null
  },
)

// A route change can tear the dialog down while it is still open, and the `open`
// watcher never runs for that. Without this the keyboard user would be left on a
// detached node — effectively back at the top of the document.
onBeforeUnmount((): void => {
  if (props.open) {
    previouslyFocused.value?.focus()
  }
})
</script>

<template>
  <Teleport to="body">
    <!-- `mousedown.self.prevent`: a press on the backdrop must not blur the panel.
         Focus on <body> is outside the trap, and the Escape and Tab handlers below
         never fire for it. Only the backdrop itself is suppressed, so presses
         inside the panel still focus and select normally. -->
    <div
      v-if="open"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/55 p-5 backdrop-blur-[2px]"
      @mousedown.self.prevent
      @click="onBackdropClick"
      @keydown.esc.prevent="requestClose"
      @keydown.tab="onTab"
    >
      <div
        ref="panelElement"
        role="dialog"
        aria-modal="true"
        :aria-labelledby="titleId"
        tabindex="-1"
        class="ui-modal-panel w-full max-w-[460px] overflow-hidden rounded-xl border border-border-strong bg-surface-1 shadow-[0_24px_64px_rgb(0_0_0/0.5)] focus-visible:outline-none"
      >
        <div class="flex items-start justify-between gap-4 px-4.5 pt-4 pb-3.5">
          <h2 :id="titleId" class="text-lg font-semibold text-text-primary">{{ title }}</h2>
          <button
            type="button"
            class="inline-flex size-6 shrink-0 items-center justify-center rounded-md text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none"
            @click="requestClose"
          >
            <!-- Decorative glyph: the button's accessible name comes from the caller-translated label. -->
            <svg class="size-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true">
              <path d="M6 6l12 12M18 6L6 18" stroke-width="2" stroke-linecap="round" />
            </svg>
            <span class="sr-only">{{ closeLabel }}</span>
          </button>
        </div>
        <div class="px-4.5 pb-4 text-base leading-normal text-text-secondary">
          <slot />
        </div>
        <!-- The design seats the actions on the raised surface, which is what
             separates them from the body without a second full-width rule. -->
        <div
          v-if="$slots.footer"
          class="flex justify-end gap-2 border-t border-border-subtle bg-surface-2 px-4.5 py-2.5"
        >
          <slot name="footer" />
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
/* The design opens every overlay with the same short rise; a keyframe is the one
   thing a utility class cannot express, so it lives here rather than in the
   token file, which this component does not own. */
.ui-modal-panel {
  animation: ui-modal-rise 0.16s ease;
}

@media (prefers-reduced-motion: reduce) {
  .ui-modal-panel {
    animation: none;
  }
}

@keyframes ui-modal-rise {
  from {
    opacity: 0;
    transform: translateY(7px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}
</style>
