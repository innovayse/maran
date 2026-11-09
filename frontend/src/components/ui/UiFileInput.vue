<script setup lang="ts">
/**
 * The panel's only file-chooser primitive. Binds a real `<label>` to a real `<input type="file">`
 * (a label is a required prop, as for {@link UiInput}) and reports the file the person chose; the
 * browser's own "Choose file" button is the control, drawn in the kit's field look, so keyboard
 * activation, focus and the operating system's file dialog all come from the platform.
 *
 * It reads nothing from the file: what to do with it (check its size, turn it into a data URI,
 * upload it) is the caller's. Choosing nothing, or cancelling the dialog, reports `null`.
 *
 * The value is never bound back to the input — browsers refuse to set a file input's value from
 * script — so the component does not pretend to be a `v-model`: it only emits `select`. After a
 * choice the native control keeps showing the chosen file's name until the caller replaces the
 * component's key or the person chooses again, which is the browser's own state to keep.
 *
 * **The edge** is `border-border-control` like every field (3:1 against a panel in both themes), and
 * the native button inside is a `bg-surface-3` chip with primary text (13.55:1 on dark).
 */
import { useId } from 'vue'

/** Props accepted by {@link UiFileInput}. */
defineProps<{
  /** Visible label text, always rendered. */
  label: string
  /** The file types the dialog offers, in the native `accept` syntax (`image/png,image/jpeg`). */
  accept?: string
  /** Disables the control. A real prop: written on the tag it would land on the wrapper `div`. */
  disabled?: boolean
}>()

/** Events emitted by {@link UiFileInput}. */
const emit = defineEmits<{
  /** Fired when the person chooses a file, or `null` when the choice is cleared. */
  (e: 'select', file: File | null): void
}>()

/** Stable, unique id pair for this instance's `<label for>`/`<input id>`. */
const inputId: string = useId()

/**
 * Reports the chosen file.
 * @param event The native change event.
 * @returns Nothing; re-emits synchronously.
 */
const onChange = (event: Event): void => {
  const files = (event.target as HTMLInputElement).files
  emit('select', files !== null && files.length > 0 ? files[0] : null)
}
</script>

<template>
  <div class="flex flex-col gap-1">
    <label :for="inputId" class="text-xs font-bold wrap-anywhere text-text-muted">{{ label }}</label>
    <input
      :id="inputId"
      type="file"
      :accept="accept"
      :disabled="disabled"
      class="min-h-control w-full rounded-control border border-border-control bg-surface-2 py-1.5 pr-3 pl-1.5 text-base text-text-primary transition-colors file:mr-3 file:cursor-pointer file:rounded-inset file:border-0 file:bg-surface-3 file:px-3 file:py-1 file:text-sm file:font-semibold file:text-text-primary enabled:hover:border-border-control-hover focus-visible:border-accent focus-visible:shadow-field-focus focus-visible:outline-none disabled:cursor-not-allowed disabled:border-border-subtle disabled:text-text-muted sm:text-sm"
      @change="onChange"
    />
  </div>
</template>
