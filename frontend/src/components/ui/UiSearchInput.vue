<script setup lang="ts">
/**
 * Search field for filtering a list. Same label/`v-model` contract as
 * `UiInput`, plus two things a plain text field cannot express: it announces
 * itself as a search box to assistive technology, and it offers a clear
 * button that both empties the value and returns focus to the field, so a
 * keyboard user is never stranded on a control that just disappeared.
 * Pressing Enter emits `search`; callers may also debounce on `update:modelValue`.
 */
import { computed, ref, useId, type ComputedRef, type Ref } from 'vue'

/** Props accepted by {@link UiSearchInput}. */
const props = withDefaults(
  defineProps<{
    /** Current query text (`v-model` target). */
    modelValue: string
    /** Visible label text, always rendered — never omitted for a placeholder instead. */
    label: string
    /** Placeholder text shown when the field is empty. */
    placeholder?: string
    /** Accessible name for the clear button, already translated by the caller. */
    clearLabel: string
  }>(),
  { placeholder: undefined },
)

/** Events emitted by {@link UiSearchInput}. */
const emit = defineEmits<{
  /** Fired on every keystroke, carrying the field's new value. */
  (e: 'update:modelValue', value: string): void
  /** Fired when the user submits the query (Enter) or clears it, carrying the value to search for. */
  (e: 'search', value: string): void
}>()

/** Stable, unique id pair for this instance's `<label for>`/`<input id>`. */
const fieldId: string = useId()

/** The underlying input element, needed to restore focus after the clear button is used. */
const inputElement: Ref<HTMLInputElement | null> = ref(null)

/**
 * Moves keyboard focus into the field.
 *
 * Exposed because a parent that OPENS a search — a jump-to palette, a filter
 * drawer — has to put the caret where the user is already typing, and reaching
 * into the component's DOM from outside would tie the caller to this markup.
 * @returns Nothing; focus moves synchronously.
 */
const focus = (): void => {
  inputElement.value?.focus()
}

defineExpose({ focus })

/** Whether there is a query to clear; the clear button is meaningless on an empty field. */
const hasValue: ComputedRef<boolean> = computed(() => props.modelValue.length > 0)

/**
 * Forwards the native input value to the `update:modelValue` emit.
 * @param event The native input event.
 * @returns Nothing; re-emits synchronously.
 */
const onInput = (event: Event): void => {
  emit('update:modelValue', (event.target as HTMLInputElement).value)
}

/**
 * Submits the current query on Enter. The field lives inside a `novalidate`
 * form on some screens and outside any form on others, so submission is
 * handled here rather than relying on implicit form submission.
 * @returns Nothing; emits synchronously.
 */
const onEnter = (): void => {
  emit('search', props.modelValue)
}

/**
 * Empties the query, re-runs the search with the empty value, and returns focus
 * to the field — the clear button hides itself once the value is gone, so
 * leaving focus on it would drop the keyboard user out of the control.
 * @returns Nothing; emits synchronously.
 */
const onClear = (): void => {
  emit('update:modelValue', '')
  emit('search', '')
  inputElement.value?.focus()
}
</script>

<template>
  <div class="flex flex-col gap-1">
    <label :for="fieldId" class="text-xs font-medium text-text-secondary">{{ label }}</label>
    <div class="relative flex items-center">
      <!-- Decorative glyph: the field is already named by its label, so the
           magnifier repeats nothing to a screen reader. -->
      <svg
        class="pointer-events-none absolute left-2 size-3.5 text-text-muted"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        aria-hidden="true"
      >
        <circle cx="11" cy="11" r="7" />
        <path d="M20 20l-4-4" />
      </svg>
      <input
        :id="fieldId"
        ref="inputElement"
        type="text"
        role="searchbox"
        :value="modelValue"
        :placeholder="placeholder"
        class="w-full rounded-lg border border-border-subtle bg-surface-2 py-1.5 pr-7.5 pl-7 text-xs text-text-primary transition-colors placeholder:text-text-muted hover:border-border-strong focus-visible:border-accent focus-visible:shadow-focus focus-visible:outline-none"
        @input="onInput"
        @keydown.enter.prevent="onEnter"
      />
      <button
        v-if="hasValue"
        type="button"
        class="absolute right-1 inline-flex size-6 items-center justify-center rounded-md text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none"
        @click="onClear"
      >
        <!-- Decorative glyph: the button's accessible name comes from the caller-translated label. -->
        <svg class="size-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true">
          <path d="M6 6l12 12M18 6L6 18" stroke-width="2" stroke-linecap="round" />
        </svg>
        <span class="sr-only">{{ clearLabel }}</span>
      </button>
    </div>
  </div>
</template>
