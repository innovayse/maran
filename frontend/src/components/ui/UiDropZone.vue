<script setup lang="ts">
/**
 * A labelled target for dropping files, with a button that opens the platform's file dialog as the
 * keyboard-equivalent way in. Dragging is a pointer gesture; nobody who cannot drag is left without
 * a route, because the button chooses the same files and emits the same event.
 *
 * It reads nothing from the files: it emits them as chosen and the caller checks sizes, names and
 * what to do next. A drop of a folder yields no usable `File` in browsers (the entry is a
 * zero-length placeholder), so the caller filters on size or offers an archive; this primitive
 * does not pretend to walk directories.
 *
 * The native `<input type="file">` is visually hidden and out of the Tab order (`tabindex="-1"`,
 * `aria-hidden`): the button is the control a person reaches, so there is exactly one stop. After a
 * choice the input's value is cleared so choosing the same file twice still fires a change.
 *
 * **Look.** A dashed `border-border-control` box (3:1 as a graphic) on the panel's raised surface;
 * while a drag is over it the edge turns `border-accent` on the `bg-accent-soft` wash. The prompt is
 * primary text and the hint muted body text. The drag highlight is a state of the zone, announced
 * to nobody: it only confirms to a pointer user that the drop will land.
 */
import { ref, type Ref } from 'vue'
import UiButton from './UiButton.vue'

/** Props accepted by {@link UiDropZone}. */
const props = withDefaults(
  defineProps<{
    /** The zone's prompt and accessible name, already translated ("Drop files here"). */
    label: string
    /** The button's text, already translated ("Choose files"). */
    pickLabel: string
    /** An optional helper sentence under the prompt, already translated (the size limit, say). */
    hint?: string
    /** The file types the dialog offers, in the native `accept` syntax; any when omitted. */
    accept?: string
    /** Whether more than one file may be chosen or dropped at once. */
    multiple?: boolean
    /** Disables the zone and its button. A real prop: written on the tag it would land on the wrapper. */
    disabled?: boolean
  }>(),
  { hint: undefined, accept: undefined, multiple: true, disabled: false },
)

/** Events emitted by {@link UiDropZone}. */
const emit = defineEmits<{
  /** Fired with the files the person dropped or chose; never empty. */
  (e: 'select', files: File[]): void
}>()

/** The hidden native input the button opens. */
const input: Ref<HTMLInputElement | null> = ref(null)

/** Whether a drag is currently over the zone. */
const dragging: Ref<boolean> = ref(false)

/**
 * Opens the platform's file dialog.
 * @returns Nothing; the dialog's answer arrives through {@link onChange}.
 */
const openDialog = (): void => {
  input.value?.click()
}

/**
 * Reports the files the dialog returned and resets the input so the same file can be chosen again.
 * @param event The native change event.
 * @returns Nothing; re-emits synchronously.
 */
const onChange = (event: Event): void => {
  const target = event.target as HTMLInputElement
  const files = target.files === null ? [] : Array.from(target.files)
  target.value = ''
  if (files.length > 0) {
    emit('select', files)
  }
}

/**
 * Marks the zone as a valid drop target while a drag is over it; without cancelling `dragover` the
 * browser would refuse the drop.
 * @param event The native dragover event.
 * @returns Nothing.
 */
const onDragOver = (event: DragEvent): void => {
  if (props.disabled) {
    // Not cancelling dragover leaves the browser refusing the drop, which is what disabled means.
    return
  }
  event.preventDefault()
  dragging.value = true
}

/**
 * Clears the highlight when the drag leaves the zone itself rather than moving onto a child of it.
 * @param event The native dragleave event.
 * @returns Nothing.
 */
const onDragLeave = (event: DragEvent): void => {
  const current = event.currentTarget as HTMLElement
  if (!current.contains(event.relatedTarget as Node | null)) {
    dragging.value = false
  }
}

/**
 * Reports the dropped files, only the first one when the zone takes a single file.
 * @param event The native drop event.
 * @param single Whether the zone accepts one file only.
 * @returns Nothing; re-emits synchronously.
 */
const onDrop = (event: DragEvent, single: boolean): void => {
  event.preventDefault()
  dragging.value = false
  if (props.disabled) {
    return
  }
  const files = event.dataTransfer === null ? [] : Array.from(event.dataTransfer.files)
  if (files.length > 0) {
    emit('select', single ? files.slice(0, 1) : files)
  }
}
</script>

<template>
  <div
    role="group"
    :aria-label="label"
    class="flex flex-col items-center gap-2 rounded-panel border border-dashed px-4 py-6 text-center transition-colors"
    :class="dragging ? 'border-accent bg-accent-soft' : 'border-border-control bg-surface-2'"
    data-testid="drop-zone"
    @dragover="onDragOver"
    @dragleave="onDragLeave"
    @drop="onDrop($event, !multiple)"
  >
    <p class="text-sm font-semibold text-text-primary wrap-anywhere">{{ label }}</p>
    <p v-if="hint !== undefined" class="text-sm text-text-muted wrap-anywhere">{{ hint }}</p>
    <UiButton variant="secondary" :disabled="disabled" @click="openDialog">{{ pickLabel }}</UiButton>
    <input
      ref="input"
      type="file"
      class="sr-only"
      tabindex="-1"
      aria-hidden="true"
      :accept="accept"
      :multiple="multiple"
      :disabled="disabled"
      @change="onChange"
    />
  </div>
</template>
