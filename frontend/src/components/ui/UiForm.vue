<script setup lang="ts">
/**
 * The panel's only form primitive. Renders a real `<form>` and intercepts
 * native submission (`@submit.prevent`) so a page never posts a full-page
 * navigation; callers listen for the `submit` emit to run their own
 * (already-validated) submit logic (rules/vue.md: "UI comes from
 * components/ui").
 *
 * `novalidate` is deliberate: the browser's own validation bubbles are
 * unstyled and untranslatable, and would compete with each field's own error
 * message (`aria-invalid` plus `aria-describedby`). It carries no layout of its
 * own — a page arranges its fields, and a default here would fight every one of
 * the grids and stacks that already do.
 *
 * **`loading`: the form is its own skeleton.** While the record the form edits is still loading,
 * the form provides `loading` to every kit field inside it (`UiInput`, `UiPasswordInput` through it,
 * `UiSelect`, `UiTextarea`, `UiCombobox`, `UiSwitch`, `UiCheckbox`). Each keeps its real,
 * translated label and swaps only its control for a placeholder of the control's footprint, so the
 * page's template stays the one description of its fields and the loaded form lands in exactly the
 * space the placeholder held. The form is `aria-busy` meanwhile; the announcement itself belongs to
 * the {@link UiLoadingRegion} around it.
 *
 * **A read-only session draws the form disabled** (issue #515). While an operator is viewing as
 * another login the session cannot write, so the form's content sits in a disabled `<fieldset>`:
 * every field and button inside is disabled by the browser itself, and Enter submits nothing. A
 * submit button outside the `<form>` (a dialog's footer) is disabled by `UiButton` on its own.
 * Advice only: the panel refuses the write whatever the browser shows.
 */
import { provide, toRef } from 'vue'
import { useSessionReadOnly } from '../../composables/useSessionReadOnly'
import { FORM_LOADING_KEY } from '../../utils/formLoadingKey'

/** Props accepted by {@link UiForm}. */
const props = withDefaults(
  defineProps<{
    /** Whether the record the form edits is still loading; its fields draw placeholders meanwhile. */
    loading?: boolean
  }>(),
  { loading: false },
)

/** Events emitted by {@link UiForm}. */
const emit = defineEmits<{
  /** Fired when the form is submitted, with the native browser submission already prevented. */
  (e: 'submit'): void
}>()

provide(FORM_LOADING_KEY, toRef(props, 'loading'))

const readOnly = useSessionReadOnly()

/**
 * Handles the native `submit` event and re-emits it as the component's own `submit` event.
 * A form still loading its record has no values to send, so Enter in it submits nothing; nor does a form of a read-only session.
 * @returns Nothing; re-emits synchronously.
 */
const onSubmit = (): void => {
  if (props.loading || readOnly.value) {
    return
  }
  emit('submit')
}
</script>

<template>
  <form novalidate :aria-busy="loading ? 'true' : undefined" @submit.prevent="onSubmit">
    <!-- `contents`: the fieldset takes no box of its own, so the form's own grid or stack still lays out its fields. -->
    <fieldset v-if="readOnly" disabled class="m-0 contents min-w-0 border-0 p-0">
      <slot />
    </fieldset>
    <slot v-else />
  </form>
</template>
