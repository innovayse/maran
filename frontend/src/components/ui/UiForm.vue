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
 */

/** Events emitted by {@link UiForm}. */
const emit = defineEmits<{
  /** Fired when the form is submitted, with the native browser submission already prevented. */
  (e: 'submit'): void
}>()

/**
 * Handles the native `submit` event and re-emits it as the component's own `submit` event.
 * @returns Nothing; re-emits synchronously.
 */
const onSubmit = (): void => {
  emit('submit')
}
</script>

<template>
  <form novalidate @submit.prevent="onSubmit">
    <slot />
  </form>
</template>
