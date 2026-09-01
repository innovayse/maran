<script setup lang="ts">
/**
 * The panel's only select primitive — a custom combobox, deliberately NOT a
 * native `<select>`: the native control's popup is drawn by the operating
 * system, so it cannot be styled, cannot show a rich row, and looks like a
 * different application on every platform the panel supports.
 *
 * The cost of not being native is that the keyboard contract has to be
 * implemented here, so it is implemented in full (ARIA combobox with a
 * listbox popup): Enter, Space, Arrow Down and Arrow Up open the list; the
 * arrows, Home and End move the active option while focus stays on the
 * trigger and `aria-activedescendant` follows; Enter selects; Escape closes
 * and returns focus; Tab or a click outside closes without selecting.
 *
 * Options come from the caller — the SPA never invents domain data
 * (rules/vue.md) — and are rendered by `UiOption`.
 */
import { computed, onBeforeUnmount, onMounted, ref, useId, watch, type ComputedRef, type Ref } from 'vue'
import { nextEnabledIndex } from '../../utils/nextEnabledIndex'
import UiIcon from './UiIcon.vue'
import UiOption from './UiOption.vue'

/**
 * One selectable entry offered by {@link UiSelect}. Declared here, not in
 * `src/types/`, because it is this component's prop shape (rules/vue.md).
 *
 * Values and labels are supplied by the caller — the SPA never invents domain
 * data, so a list of plans, statuses or tiers arrives from the backend already
 * localized, while a purely cosmetic list (a page-size chooser, say) is
 * translated by the caller before it reaches the primitive.
 */
export interface SelectOption {
  /** Stable machine value bound through `v-model` when this entry is chosen. */
  value: string
  /** Ready-to-render text for the entry; the primitive holds no copy of its own. */
  label: string
  /** Whether the entry is present but not choosable — skipped by keyboard navigation. */
  disabled?: boolean
}

/** Props accepted by {@link UiSelect}. */
const props = withDefaults(
  defineProps<{
    /** Value of the selected option (`v-model` target); empty string when nothing is selected. */
    modelValue: string
    /** Label text, always rendered — never omitted for a placeholder instead. */
    label: string
    /**
     * Hides the label visually while keeping it in the accessibility tree.
     *
     * For a control whose meaning is obvious from its own value — a language
     * picker showing "English" — in a bar with no room for a caption. It is
     * never a way to skip naming the field: a screen reader still announces it,
     * which is why this hides the label rather than dropping it.
     */
    hideLabel?: boolean
    /** Options to offer, already localized by the caller or by the backend. */
    options: readonly SelectOption[]
    /** Text shown on the trigger while nothing is selected, already translated by the caller. */
    placeholder?: string
    /** Disables the control and marks it non-interactive for assistive tech. */
    disabled?: boolean
    /**
     * Marks the field required for assistive technology. Rendered as `aria-required`, not the
     * native `required` attribute: forms are `novalidate` (rules/vue.md).
     */
    required?: boolean
    /** Already-translated validation message; when set, the field renders as invalid. */
    error?: string | null
  }>(),
  { placeholder: undefined, hideLabel: false, disabled: false, required: false, error: null },
)

/** Events emitted by {@link UiSelect}. */
const emit = defineEmits<{
  /** Fired when the selection changes, carrying the newly selected option's value. */
  (e: 'update:modelValue', value: string): void
}>()

/** Stable, unique ids tying the label, trigger, listbox and error message together. */
const fieldId: string = useId()
const labelId: string = `${fieldId}-label`
const listboxId: string = `${fieldId}-listbox`
const errorId: string = `${fieldId}-error`

/** Whether the option list is currently open. */
const isOpen: Ref<boolean> = ref(false)

/** Index of the keyboard's current position in {@link props.options}, or -1 when there is none. */
const activeIndex: Ref<number> = ref(-1)

/** The component's outermost element, used to tell an outside click from an inside one. */
const rootElement: Ref<HTMLElement | null> = ref(null)

/** The trigger button, which focus returns to whenever the list closes. */
const triggerElement: Ref<HTMLButtonElement | null> = ref(null)

/** Whether the field is currently in an error state. */
const hasError: ComputedRef<boolean> = computed(() => {
  return props.error !== null && props.error !== undefined
})

/** The currently selected option, or `null` when the model value matches none. */
const selectedOption: ComputedRef<SelectOption | null> = computed(() => {
  return (
    props.options.find((option: SelectOption): boolean => {
      return option.value === props.modelValue
    }) ?? null
  )
})

/** Text rendered on the trigger: the selection's label, or the placeholder while nothing is selected. */
const triggerText: ComputedRef<string> = computed(() => {
  return selectedOption.value?.label ?? props.placeholder ?? ''
})

/** DOM id of the active option, published to assistive tech as `aria-activedescendant`. */
const activeOptionId: ComputedRef<string | undefined> = computed(() => {
  return isOpen.value && activeIndex.value >= 0 ? `${fieldId}-option-${String(activeIndex.value)}` : undefined
})

/**
 * Builds the DOM id for the option at a position. Ids must be derivable from the
 * index alone, because `aria-activedescendant` is resolved by id, not by node.
 * @param index Position of the option in {@link props.options}.
 * @returns The option row's DOM id.
 */
const optionId = (index: number): string => {
  return `${fieldId}-option-${String(index)}`
}

/**
 * Index the list should open on: the current selection when there is one, else the
 * first choosable option — so a keyboard user starts somewhere meaningful.
 * @returns The index to make active on open, or -1 when no option is choosable.
 */
const initialActiveIndex = (): number => {
  const selected = props.options.findIndex((option: SelectOption): boolean => {
    return option.value === props.modelValue
  })
  if (selected >= 0 && props.options[selected]?.disabled !== true) {
    return selected
  }
  return nextEnabledIndex(props.options, -1, 1)
}

/**
 * Opens the option list and places the active position.
 * @returns Nothing; state updates synchronously.
 */
const open = (): void => {
  if (props.disabled) {
    return
  }
  isOpen.value = true
  activeIndex.value = initialActiveIndex()
}

/**
 * Closes the option list and returns focus to the trigger, so the keyboard user
 * lands back on the control they opened rather than at the top of the document.
 * @returns Nothing; state updates synchronously.
 */
const close = (): void => {
  isOpen.value = false
  activeIndex.value = -1
  triggerElement.value?.focus()
}

/**
 * Closes the list without moving focus. Used for dismissals the user did not
 * initiate from the keyboard (an outside click, a Tab away), where stealing
 * focus back would fight what the user just did.
 * @returns Nothing; state updates synchronously.
 */
const dismiss = (): void => {
  isOpen.value = false
  activeIndex.value = -1
}

/**
 * Publishes a selection and closes the list.
 * @param value Value of the chosen option.
 * @returns Nothing; emits synchronously.
 */
const select = (value: string): void => {
  emit('update:modelValue', value)
  close()
}

/**
 * Commits the option the keyboard is currently on.
 * @returns Nothing; emits synchronously.
 */
const selectActive = (): void => {
  const option = activeIndex.value >= 0 ? props.options[activeIndex.value] : undefined
  if (option !== undefined && option.disabled !== true) {
    select(option.value)
  }
}

/**
 * Moves the keyboard position, opening the list first when it is closed — the
 * arrow keys are an "open and move" gesture in the combobox pattern.
 * @param step Direction: 1 forwards, -1 backwards.
 * @returns Nothing; state updates synchronously.
 */
const move = (step: number): void => {
  if (!isOpen.value) {
    open()
    return
  }
  activeIndex.value = nextEnabledIndex(props.options, activeIndex.value, step)
}

/**
 * Jumps the keyboard position to the first or last choosable option.
 * @param edge Which end to jump to.
 * @returns Nothing; state updates synchronously.
 */
const moveToEdge = (edge: 'first' | 'last'): void => {
  if (!isOpen.value) {
    open()
  }
  activeIndex.value =
    edge === 'first'
      ? nextEnabledIndex(props.options, -1, 1)
      : nextEnabledIndex(props.options, props.options.length, -1)
}

/**
 * Toggles the list from the trigger's click.
 * @returns Nothing; state updates synchronously.
 */
const onTriggerClick = (): void => {
  if (isOpen.value) {
    close()
    return
  }
  open()
}

/**
 * Handles Enter and Space on the trigger: both open the list, and Enter commits
 * the active option once it is open.
 * @param event The keyboard event, whose default (page scroll on Space, form
 * submission on Enter) must be suppressed.
 * @returns Nothing; state updates synchronously.
 */
const onConfirmKey = (event: KeyboardEvent): void => {
  event.preventDefault()
  if (!isOpen.value) {
    open()
    return
  }
  selectActive()
}

/**
 * Closes the list when focus leaves the component entirely (a Tab away). The
 * check is against the element receiving focus, so moving between the trigger
 * and the list does not close it.
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
 * Closes the list on a click outside the component. Bound to `mousedown` rather
 * than `click` so the list is gone before the outside target reacts.
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

onMounted((): void => {
  document.addEventListener('mousedown', onDocumentMouseDown)
})

onBeforeUnmount((): void => {
  document.removeEventListener('mousedown', onDocumentMouseDown)
})

// An option list that shrinks under an open popup can leave the active position
// past its end; re-anchor it instead of pointing `aria-activedescendant` at an
// id that no longer exists.
watch(
  (): number => {
    return props.options.length
  },
  (length: number): void => {
    if (isOpen.value && activeIndex.value >= length) {
      activeIndex.value = nextEnabledIndex(props.options, length, -1)
    }
  },
)
</script>

<template>
  <div ref="rootElement" class="flex flex-col gap-1" @focusout="onFocusOut">
    <span
      :id="labelId"
      class="text-base font-medium"
      :class="[hasError ? 'text-danger' : 'text-text-secondary', hideLabel ? 'sr-only' : '']"
      >{{ label }}</span
    >
    <div class="relative">
      <button
        ref="triggerElement"
        type="button"
        role="combobox"
        :aria-expanded="isOpen"
        aria-haspopup="listbox"
        :aria-controls="isOpen ? listboxId : undefined"
        :aria-activedescendant="activeOptionId"
        :aria-labelledby="labelId"
        :aria-required="required ? 'true' : undefined"
        :aria-invalid="hasError"
        :aria-describedby="hasError ? errorId : undefined"
        :disabled="disabled"
        class="flex w-full items-center justify-between gap-2 rounded-lg border bg-surface-2 px-2 py-1.5 text-left text-base text-text-primary transition-colors focus-visible:outline-none disabled:cursor-not-allowed disabled:text-text-muted disabled:opacity-65"
        :class="
          hasError
            ? 'border-[rgb(229_72_77/0.5)] focus-visible:shadow-focus-danger'
            : 'border-border-subtle focus-visible:border-accent focus-visible:shadow-focus'
        "
        @click="onTriggerClick"
        @keydown.enter="onConfirmKey"
        @keydown.space="onConfirmKey"
        @keydown.down.prevent="move(1)"
        @keydown.up.prevent="move(-1)"
        @keydown.home.prevent="moveToEdge('first')"
        @keydown.end.prevent="moveToEdge('last')"
        @keydown.esc.prevent="close"
      >
        <span :class="selectedOption === null ? 'text-text-muted' : ''">{{ triggerText }}</span>
        <UiIcon name="chevronDown" :size="12" :stroke-width="2" class="text-text-muted" />
      </button>
      <!-- The rows are not focusable, so a plain mousedown on one would move focus
           to the body, fire `focusout` on the trigger and dismiss the list before
           the click ever reached the row. Suppressing the default keeps focus on
           the trigger, which is where the listbox pattern wants it anyway. -->
      <ul
        v-if="isOpen"
        :id="listboxId"
        role="listbox"
        :aria-labelledby="labelId"
        class="absolute z-10 mt-1 max-h-60 w-full overflow-y-auto rounded-lg border border-border-strong bg-surface-2 p-1 shadow-[0_12px_32px_rgb(0_0_0/0.4)]"
        @mousedown.prevent
      >
        <UiOption
          v-for="(option, index) in options"
          :id="optionId(index)"
          :key="option.value"
          :value="option.value"
          :label="option.label"
          :selected="option.value === modelValue"
          :active="index === activeIndex"
          :disabled="option.disabled === true"
          @select="select"
          @activate="activeIndex = index"
        />
      </ul>
    </div>
    <p v-if="hasError" :id="errorId" class="text-base text-danger">{{ error }}</p>
  </div>
</template>
