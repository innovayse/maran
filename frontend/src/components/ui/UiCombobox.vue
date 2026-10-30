<script setup lang="ts">
/**
 * A picker whose choices the SERVER searches: the select for a set too large to hold in the
 * browser (accounts — a panel may have thousands). It looks and is found exactly like
 * {@link UiSelect}: the closed control is the same trigger, a `button` with role `combobox` named by
 * the visible label and showing the selection or the placeholder and nothing else. Opening it drops
 * a popup holding a search field and the current page of matches, rendered by `UiOption`.
 *
 * A primitive of its own, not a UiSelect mode: here focus moves into a text field and the popup
 * carries loading, refusal and "more matches" lines a caller-held list never has.
 *
 * The search is the caller's: `search` is emitted with an empty term when the popup opens, then
 * with the typed text once typing pauses (`useDebouncedSearch`), and whatever `options`, `total`,
 * `loading` and `loadError` come back is rendered. The caller drops stale answers (the store does).
 *
 * Keyboard, as in UiSelect: Enter, Space and the arrows on the trigger open the popup with focus in
 * the field; the arrows move the active option (`aria-activedescendant`), Enter selects, Escape
 * closes and returns focus, Tab or an outside press (`useOutsidePress`) closes without selecting.
 * Home and End stay with the text field's caret.
 *
 * THE CHOSEN NAME STAYS SHOWN: the label of an option chosen here is remembered, since the next
 * page of matches rarely holds it; a value set from outside (a URL) comes with `selectedLabel`.
 *
 * LOADING. Inside a {@link UiForm} with `loading`, the label stays (it is the form's real text)
 * and the trigger is replaced by a {@link UiSkeleton} of its footprint, so the loaded field lands
 * in the same space. That is the form's flag, read as `formLoading`; this component's own
 * `loading` prop is a search in flight and is unrelated.
 */
import { computed, nextTick, ref, useId, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useDebouncedSearch } from '../../composables/useDebouncedSearch'
import { useFormLoading } from '../../composables/useFormLoading'
import { useOutsidePress } from '../../composables/useOutsidePress'
import UiIcon from './UiIcon.vue'
import UiOption from './UiOption.vue'
import UiSkeleton from './UiSkeleton.vue'

/** One entry offered by {@link UiCombobox}; this component's own prop shape (rules/vue.md). */
export interface ComboboxOption {
  /** Stable machine value bound through `v-model` when this entry is chosen. */
  value: string
  /** Ready-to-render text for the entry, already localized by the caller or the backend. */
  label: string
}

/** Props accepted by {@link UiCombobox}. */
const props = withDefaults(
  defineProps<{
    /** Value of the selected option (`v-model` target); empty string when nothing is selected. */
    modelValue: string
    /** Label text, always rendered; the trigger's accessible name. */
    label: string
    /** Accessible name of the search field inside the popup, already translated. */
    searchLabel: string
    /** The current page of matches for the last search, already localized. */
    options: readonly ComboboxOption[]
    /** How many matches exist in all; when larger than `options`, the popup says how many more. */
    total: number
    /** Whether the read for the last search is still in flight. */
    loading?: boolean
    /** The caller's already-localized reason the last search failed; `null` while it has not. */
    loadError?: string | null
    /** The label of a value set from outside this control, shown until a choice is made here. */
    selectedLabel?: string
    /** Text shown on the trigger while nothing is selected, already translated. */
    placeholder?: string
    /** Disables the control and marks it non-interactive for assistive tech. */
    disabled?: boolean
    /** Marks the field required for assistive technology (`aria-required`; forms are `novalidate`). */
    required?: boolean
    /** Already-translated validation message; when set, the field renders as invalid. */
    error?: string | null
  }>(),
  {
    loading: false,
    loadError: null,
    selectedLabel: undefined,
    placeholder: undefined,
    disabled: false,
    required: false,
    error: null,
  },
)

/** Events emitted by {@link UiCombobox}. */
const emit = defineEmits<{
  /** Fired when an option is chosen, carrying its value. */
  (e: 'update:modelValue', value: string): void
  /** Fired when the matches must be read again, carrying the text typed so far. */
  (e: 'search', term: string): void
}>()

/** How long typing must pause before a search is sent, in milliseconds: a UI pacing constant. */
const SEARCH_DEBOUNCE_MS = 250

const { t } = useI18n()

/** Stable, unique ids tying the label, trigger, popup, options and error message together. */
const fieldId: string = useId()
const labelId: string = `${fieldId}-label`
const listboxId: string = `${fieldId}-listbox`
const errorId: string = `${fieldId}-error`

/** Whether the enclosing form is still loading, so the trigger is drawn as a placeholder. */
const formLoading = useFormLoading()

/** Whether the popup is open. */
const isOpen: Ref<boolean> = ref(false)

/** Index of the keyboard's position in {@link props.options}, or -1 when there is none. */
const activeIndex: Ref<number> = ref(-1)

/** The option last chosen here, so its label outlives the page of matches it came from. */
const chosen: Ref<ComboboxOption | null> = ref(null)

/** The outermost element, used to tell an outside click or focus move from an inside one. */
const rootElement: Ref<HTMLElement | null> = ref(null)

/** The trigger, which focus returns to whenever the popup closes from the keyboard. */
const triggerElement: Ref<HTMLButtonElement | null> = ref(null)

/** The search field, which takes focus when the popup opens. */
const searchElement: Ref<HTMLInputElement | null> = ref(null)

/** The search field's text, sent to the parent once typing pauses. */
const search = useDebouncedSearch((text: string): void => {
  emit('search', text)
}, SEARCH_DEBOUNCE_MS)

/** Whether the field is in an error state. */
const hasError: ComputedRef<boolean> = computed(() => {
  return props.error !== null && props.error !== undefined
})

/** The trigger's text: the selection's label, else the placeholder while nothing is selected. */
const triggerText: ComputedRef<string> = computed(() => {
  if (props.modelValue === '') {
    return props.placeholder ?? ''
  }
  const onPage = props.options.find((option: ComboboxOption): boolean => {
    return option.value === props.modelValue
  })
  if (onPage !== undefined) {
    return onPage.label
  }
  if (chosen.value?.value === props.modelValue) {
    return chosen.value.label
  }
  return props.selectedLabel ?? props.modelValue
})

/** How many matches the page leaves out; zero when it holds them all. */
const hiddenCount: ComputedRef<number> = computed(() => {
  return Math.max(props.total - props.options.length, 0)
})

/** The one line the popup shows instead of options, or the empty string when options are shown. */
const statusLine: ComputedRef<string> = computed(() => {
  if (props.loading) {
    return t('app.combobox.loading')
  }
  if (props.loadError !== null && props.loadError !== '') {
    return props.loadError
  }
  return props.options.length === 0 ? t('app.combobox.noMatches') : ''
})

/** DOM id of the active option, published as `aria-activedescendant` on the search field. */
const activeOptionId: ComputedRef<string | undefined> = computed(() => {
  return isOpen.value && statusLine.value === '' && activeIndex.value >= 0
    ? optionId(activeIndex.value)
    : undefined
})

/**
 * Builds the DOM id of the option at a position.
 * @param index Position of the option in {@link props.options}.
 * @returns The option row's DOM id.
 */
const optionId = (index: number): string => {
  return `${fieldId}-option-${String(index)}`
}

/**
 * Opens the popup on an empty search, asks for the first page and moves focus into the field.
 * @returns Resolves once focus has moved.
 */
const open = async (): Promise<void> => {
  if (props.disabled || isOpen.value) {
    return
  }
  search.reset()
  isOpen.value = true
  emit('search', '')
  await nextTick()
  searchElement.value?.focus()
}

/**
 * Closes the popup; from the keyboard focus returns to the trigger, from a click elsewhere it stays
 * where the user put it.
 * @param refocus Whether to move focus back to the trigger.
 * @returns Nothing.
 */
const close = (refocus: boolean): void => {
  search.cancel()
  isOpen.value = false
  activeIndex.value = -1
  if (refocus) {
    triggerElement.value?.focus()
  }
}

/**
 * Publishes a choice, remembers its label, and closes the popup.
 * @param value Value of the chosen option.
 * @returns Nothing.
 */
const select = (value: string): void => {
  const option = props.options.find((candidate: ComboboxOption): boolean => {
    return candidate.value === value
  })
  if (option === undefined) {
    return
  }
  chosen.value = option
  emit('update:modelValue', value)
  close(true)
}

/**
 * Records a keystroke in the search field and schedules the search for it.
 * @param event The native input event.
 * @returns Nothing.
 */
const onInput = (event: Event): void => {
  search.type((event.target as HTMLInputElement).value)
}

/**
 * Moves the active option within the current page.
 * @param step Direction: 1 forwards, -1 backwards.
 * @returns Nothing.
 */
const move = (step: number): void => {
  activeIndex.value = Math.min(Math.max(activeIndex.value + step, 0), props.options.length - 1)
}

/**
 * Commits the active option on Enter; never submits the surrounding form.
 * @returns Nothing.
 */
const onEnter = (): void => {
  const option = props.options[activeIndex.value]
  if (statusLine.value === '' && option !== undefined) {
    select(option.value)
  }
}

/**
 * Toggles the popup from a click on the trigger.
 * @returns Nothing.
 */
const onTriggerClick = (): void => {
  if (isOpen.value) {
    close(true)
    return
  }
  void open()
}

/**
 * Keeps focus in the search field when the popup is pressed anywhere but the field itself: the rows
 * are not focusable, so the press would otherwise move focus to the body and close the popup.
 * @param event The pointer press inside the popup.
 * @returns Nothing.
 */
const onPopupMouseDown = (event: MouseEvent): void => {
  if (event.target !== searchElement.value) {
    event.preventDefault()
  }
}

/**
 * Closes the popup when focus leaves the component entirely (a Tab away).
 * @param event The native focusout event.
 * @returns Nothing.
 */
const onFocusOut = (event: FocusEvent): void => {
  const next = event.relatedTarget
  if (isOpen.value && !(next instanceof Node && rootElement.value?.contains(next) === true)) {
    close(false)
  }
}

// A new page of matches puts the keyboard on its first choosable row: the old index pointed into
// the previous search's rows.
watch(
  (): readonly ComboboxOption[] => {
    return props.options
  },
  (options: readonly ComboboxOption[]): void => {
    activeIndex.value = options.length > 0 ? 0 : -1
  },
  { immediate: true },
)

// A press outside the component closes the popup without moving focus.
useOutsidePress(rootElement, isOpen, (): void => {
  close(false)
})
</script>

<template>
  <div ref="rootElement" class="flex min-w-24 flex-col gap-1" @focusout="onFocusOut">
    <span
      :id="labelId"
      class="text-xs font-bold wrap-anywhere"
      :class="hasError ? 'text-danger' : 'text-text-muted'"
      >{{ label }}</span
    >
    <UiSkeleton v-if="formLoading" shape="block" control="field" />
    <div v-else class="relative">
      <!-- The same trigger as UiSelect, so the two pickers look, size and announce alike. -->
      <button
        ref="triggerElement"
        type="button"
        role="combobox"
        :aria-expanded="isOpen"
        aria-haspopup="listbox"
        :aria-controls="isOpen ? listboxId : undefined"
        :aria-labelledby="labelId"
        :title="modelValue === '' ? undefined : triggerText"
        :aria-required="required ? 'true' : undefined"
        :aria-invalid="hasError"
        :aria-describedby="hasError ? errorId : undefined"
        :disabled="disabled"
        class="flex min-h-control w-full items-center justify-between gap-2 rounded-control border bg-surface-2 px-3 py-1.5 text-left text-base text-text-primary transition-colors focus-visible:outline-none disabled:cursor-not-allowed disabled:text-text-muted disabled:opacity-65 sm:text-sm"
        :class="
          hasError
            ? 'border-danger focus-visible:shadow-field-focus-danger'
            : 'border-border-control enabled:hover:border-text-muted focus-visible:border-accent focus-visible:shadow-field-focus'
        "
        @click="onTriggerClick"
        @keydown.enter.prevent="open"
        @keydown.space.prevent="open"
        @keydown.down.prevent="open"
        @keydown.up.prevent="open"
      >
        <span class="min-w-0 truncate" :class="modelValue === '' ? 'text-text-muted' : ''">{{ triggerText }}</span>
        <UiIcon
          name="chevronDown"
          size="sm"
          class="shrink-0 text-text-secondary transition-transform motion-reduce:transition-none"
          :class="isOpen ? 'rotate-180' : ''"
        />
      </button>
      <!-- The popup, raised like UiSelect's. Rows are not focusable, so a press on the popup keeps
           focus in the field (onPopupMouseDown) instead of dropping it to the body. -->
      <div
        v-if="isOpen"
        class="absolute z-10 mt-1 flex w-full flex-col gap-1 rounded-control border border-border-strong bg-surface-3 p-menu shadow-lg"
        @mousedown="onPopupMouseDown"
      >
        <input
          ref="searchElement"
          type="text"
          role="searchbox"
          autocomplete="off"
          :aria-label="searchLabel"
          :aria-controls="listboxId"
          :aria-activedescendant="activeOptionId"
          :value="search.query.value"
          class="min-h-control w-full rounded-inset border border-border-control bg-surface-2 px-3 py-1.5 text-base text-text-primary focus-visible:border-accent focus-visible:shadow-field-focus focus-visible:outline-none sm:text-sm"
          @input="onInput"
          @keydown.down.prevent="move(1)"
          @keydown.up.prevent="move(-1)"
          @keydown.enter.prevent="onEnter"
          @keydown.esc.prevent.stop="close(true)"
        />
        <!-- One line instead of rows while the read is in flight, failed or found nothing. -->
        <p
          v-if="statusLine !== ''"
          :id="listboxId"
          role="status"
          class="px-2.5 py-1.5 text-sm text-text-primary wrap-anywhere"
        >
          {{ statusLine }}
        </p>
        <ul v-else :id="listboxId" role="listbox" :aria-labelledby="labelId" class="max-h-60 overflow-y-auto">
          <UiOption
            v-for="(option, index) in options"
            :id="optionId(index)"
            :key="option.value"
            :value="option.value"
            :label="option.label"
            :selected="option.value === modelValue"
            :active="index === activeIndex"
            @select="select"
            @activate="activeIndex = index"
          />
        </ul>
        <p v-if="statusLine === '' && hiddenCount > 0" class="px-2.5 py-1 text-sm text-text-secondary">
          {{ t('app.combobox.more', { count: hiddenCount }) }}
        </p>
      </div>
    </div>
    <p v-if="hasError" :id="errorId" class="text-sm text-danger wrap-anywhere">{{ error }}</p>
  </div>
</template>
