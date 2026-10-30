<script setup lang="ts">
/**
 * Where a list's filters are edited: a bottom sheet on a phone or tablet, a centred dialog from
 * `lg` up. The caller puts the fields in the default slot, bound to the slot's `draft`; nothing the
 * reader changes there reaches the list until Apply.
 *
 * **Draft.** On every opening the draft is a fresh copy of `modelValue` (the applied filters), so
 * closing without Apply (Escape, the backdrop, the close control) discards the edits. Reset empties
 * every field of the draft; it too waits for Apply, so one gesture commits and the reader can still
 * back out of a reset. Apply emits `update:modelValue` with the draft and then `close`.
 *
 * **Which surface.** From `lg` (1024px) up it is `UiModal`, the kit's dialog. Below it, a sheet
 * anchored to the bottom edge, where a thumb reaches its buttons. The choice needs a different DOM,
 * not a different look, so it is made with {@link useMediaQuery} rather than CSS.
 *
 * **The sheet's dialog contract** is the modal's: `role="dialog"`, `aria-modal`, named by its
 * title, the shared focus trap (focus moves in, Tab wraps, focus returns to the trigger on close),
 * Escape and a backdrop click close it. It slides up in 300ms and down in 200ms, and simply appears
 * and disappears under `prefers-reduced-motion`. It is teleported to `<body>`, like the modal.
 *
 * The caller owns `open`: the component reports `close` and never hides itself.
 */
import { ref, useId, watch, type Ref } from 'vue'
import { useFocusTrap } from '../../composables/useFocusTrap'
import { useMediaQuery } from '../../composables/useMediaQuery'
import UiButton from './UiButton.vue'
import UiIcon from './UiIcon.vue'
import UiModal from './UiModal.vue'

/** Props accepted by {@link UiFilterSheet}. */
const props = defineProps<{
  /** Whether the filters are shown; owned by the caller. */
  open: boolean
  /** The applied filters by name; every field the slot edits has a key, empty when unset. */
  modelValue: Record<string, string>
  /** The heading, already translated ("Filters"); also the dialog's accessible name. */
  title: string
  /** The close control's accessible name, already translated. */
  closeLabel: string
  /** The commit action's label, already translated ("Apply"). */
  applyLabel: string
  /** The clearing action's label, already translated ("Reset"). */
  resetLabel: string
}>()

/** Events emitted by {@link UiFilterSheet}. */
const emit = defineEmits<{
  /** The reader applied the draft; carries every field, empty when cleared. */
  (e: 'update:modelValue', value: Record<string, string>): void
  /** The reader dismissed the filters, or applied them. */
  (e: 'close'): void
}>()

/** Slots of {@link UiFilterSheet}. */
defineSlots<{
  /** The filter fields, bound to `draft` (`v-model="draft.status"`). */
  default: (props: { draft: Record<string, string> }) => unknown
}>()

/** From this width up the filters open as a centred dialog; Tailwind's `lg`. */
const DIALOG_QUERY = '(min-width: 64rem)'

const wide: Readonly<Ref<boolean>> = useMediaQuery(DIALOG_QUERY)

/** Links the sheet to its heading through `aria-labelledby`. */
const titleId: string = useId()

/** The edits not yet applied. */
const draft: Ref<Record<string, string>> = ref({})

/** The sheet panel, whose focusable descendants the trap cycles through. */
const sheetElement: Ref<HTMLElement | null> = ref(null)

/**
 * Whether the bottom sheet (not the dialog) is the open surface.
 * @returns True while open below `lg`.
 */
const sheetOpen = (): boolean => {
  return props.open && !wide.value
}

/**
 * Reports a dismissal; the caller closes.
 * @returns Nothing.
 */
const requestClose = (): void => {
  emit('close')
}

/**
 * Empties every field of the draft. Applied only by Apply.
 * @returns Nothing.
 */
const reset = (): void => {
  draft.value = Object.fromEntries(Object.keys(draft.value).map((key) => {return [key, '']}))
}

/**
 * Commits the draft and closes.
 * @returns Nothing.
 */
const apply = (): void => {
  emit('update:modelValue', { ...draft.value })
  emit('close')
}

/**
 * Dismisses on a click on the backdrop itself, never on one that bubbled out of the sheet.
 * @param event The click on the backdrop.
 * @returns Nothing.
 */
const onBackdropClick = (event: MouseEvent): void => {
  if (event.target === event.currentTarget) {
    requestClose()
  }
}

const { onTab } = useFocusTrap({ panel: sheetElement, open: sheetOpen })

// Every opening starts from what is applied, so edits abandoned last time are gone.
watch(
  (): boolean => {return props.open},
  (isOpen: boolean): void => {
    if (isOpen) {
      draft.value = { ...props.modelValue }
    }
  },
  { immediate: true },
)
</script>

<template>
  <UiModal v-if="wide" :open="open" :title="title" :close-label="closeLabel" @close="requestClose">
    <div class="flex flex-col gap-4" data-testid="filter-fields">
      <slot :draft="draft" />
    </div>
    <template #footer>
      <UiButton variant="secondary" @click="reset">{{ resetLabel }}</UiButton>
      <UiButton @click="apply">{{ applyLabel }}</UiButton>
    </template>
  </UiModal>

  <Teleport v-else to="body">
    <Transition name="ui-filter-sheet">
      <!-- z-[60]: the same layer as UiModal, above teleported dropdown panels (z-50). -->
      <div
        v-if="open"
        class="fixed inset-0 z-[60] flex items-end bg-page/70 backdrop-blur-sm"
        data-testid="filter-sheet-backdrop"
        @mousedown.self.prevent
        @click="onBackdropClick"
        @keydown.esc.prevent="requestClose"
        @keydown.tab="onTab"
      >
        <div
          ref="sheetElement"
          role="dialog"
          aria-modal="true"
          :aria-labelledby="titleId"
          tabindex="-1"
          class="ui-filter-sheet-panel flex max-h-sheet w-full flex-col overflow-hidden rounded-t-panel border-t border-border-strong bg-surface-1 shadow-2xl focus-visible:outline-none"
          data-testid="filter-sheet"
        >
          <div class="flex shrink-0 items-start justify-between gap-3 px-5 pt-4 pb-2">
            <h2 :id="titleId" class="min-w-0 font-display text-base font-extrabold text-text-primary wrap-anywhere">
              {{ title }}
            </h2>
            <UiButton variant="quiet" size="icon" :aria-label="closeLabel" @click="requestClose">
              <UiIcon name="x" size="sm" />
            </UiButton>
          </div>
          <!-- The fields scroll; the title and the actions stay put, so Apply is always reachable. -->
          <div
            class="flex min-h-0 flex-1 scroll-pb-5 flex-col gap-4 overflow-y-auto px-5 pt-1 pb-5 wrap-anywhere"
            data-testid="filter-fields"
          >
            <slot :draft="draft" />
          </div>
          <div class="flex shrink-0 flex-wrap justify-end gap-2 border-t border-border-subtle px-5 py-3">
            <UiButton variant="secondary" @click="reset">{{ resetLabel }}</UiButton>
            <UiButton @click="apply">{{ applyLabel }}</UiButton>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
/* The backdrop fades and the sheet slides up from the bottom edge; leaving is quicker. */
.ui-filter-sheet-enter-active,
.ui-filter-sheet-enter-active .ui-filter-sheet-panel {
  transition:
    opacity 300ms ease-out,
    transform 300ms ease-out;
}

.ui-filter-sheet-leave-active,
.ui-filter-sheet-leave-active .ui-filter-sheet-panel {
  transition:
    opacity 200ms ease-in,
    transform 200ms ease-in;
}

.ui-filter-sheet-enter-from,
.ui-filter-sheet-leave-to {
  opacity: 0;
}

.ui-filter-sheet-enter-from .ui-filter-sheet-panel,
.ui-filter-sheet-leave-to .ui-filter-sheet-panel {
  transform: translateY(100%);
}

/* For a reader who asked for less motion the sheet simply appears and disappears. */
@media (prefers-reduced-motion: reduce) {
  .ui-filter-sheet-enter-active,
  .ui-filter-sheet-enter-active .ui-filter-sheet-panel,
  .ui-filter-sheet-leave-active,
  .ui-filter-sheet-leave-active .ui-filter-sheet-panel {
    transition: none;
  }
}
</style>
