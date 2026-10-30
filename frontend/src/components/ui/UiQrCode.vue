<script setup lang="ts">
/**
 * A QR code for a piece of text, encoded in the browser and never sent anywhere.
 *
 * The one place the panel turns text into a scannable code, so the encoder (`uqr`, zero
 * dependencies) is imported here and nowhere else — the same arrangement `UiIcon` has with its icon
 * set. The input is often a secret (an `otpauth://` URI carries the two-factor seed), which is why
 * the encoding happens locally: no image service, no request, nothing written.
 *
 * Rendered as a grid of squares rather than an `<svg>` or a `<canvas>`: the SPA hand-draws no SVG
 * (rules/vue.md), and a grid needs no colour written in script. The modules are black on white in
 * both themes on purpose — a scanner expects dark on light, and an inverted code fails on many
 * phones — and the quiet zone is the encoder's own border, part of the same white square.
 *
 * It is a single image to assistive technology: `role="img"` with the caller's translated `label`,
 * whose squares are presentational children. The text it encodes is shown beside it by the caller,
 * so nothing is available only through the picture.
 */
import { computed, type ComputedRef } from 'vue'
import { encode } from 'uqr'

/** Props accepted by {@link UiQrCode}. */
const props = defineProps<{
  /** The text to encode. */
  value: string
  /** Translated accessible name of the image, e.g. "QR code for your authenticator app". */
  label: string
}>()

/** The encoded matrix, `true` for a dark module, with the encoder's quiet-zone border included. */
const modules: ComputedRef<boolean[][]> = computed(() => {
  return encode(props.value, { border: 2 }).data
})

/** One CSS grid column per module, so the squares stay square at any width. */
const gridStyle: ComputedRef<Record<string, string>> = computed(() => {
  return { gridTemplateColumns: `repeat(${String(modules.value.length)}, minmax(0, 1fr))` }
})
</script>

<template>
  <div
    role="img"
    :aria-label="label"
    class="grid aspect-square w-48 max-w-full rounded-inset bg-white"
    :style="gridStyle"
  >
    <template v-for="(row, rowIndex) in modules" :key="rowIndex">
      <span
        v-for="(dark, columnIndex) in row"
        :key="`${String(rowIndex)}-${String(columnIndex)}`"
        :class="dark ? 'bg-black' : 'bg-white'"
      />
    </template>
  </div>
</template>
