<script setup lang="ts">
/**
 * The panel's only form primitive. Renders a real `<form>` and intercepts
 * native submission (`@submit.prevent`) so a page never posts a full-page
 * navigation; callers listen for the `submit` emit to run their own
 * (already-validated) submit logic (rules/vue.md: "UI comes from
 * components/ui").
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
