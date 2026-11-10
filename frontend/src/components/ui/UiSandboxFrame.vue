<script setup lang="ts">
/**
 * The one place the panel puts third-party code on a screen: an `<iframe>` whose sandbox is fixed
 * inside this component.
 *
 * The sandbox is exactly `allow-scripts`, never with `allow-same-origin`. That pair is what gives
 * the framed document an opaque origin: it cannot read the panel's cookies, local storage or
 * scripts, cannot submit forms or open windows, and can only talk to the shell over a channel the
 * shell hands it. The sandbox is not a prop on purpose: a caller choosing its own value is how the
 * opaque origin would be lost, so a screen that needs another kind of frame extends this
 * component with a closed choice instead of passing a string.
 *
 * `referrerpolicy="no-referrer"` keeps the panel's address out of the frame's requests, and the
 * frame loads eagerly so the handshake deadline starts when the screen does.
 *
 * The `title` is required and already translated: an `<iframe>` with no accessible name is read by
 * a screen reader as an unnamed frame. The component exposes its element so the caller can reach
 * `contentWindow` for the handshake; nothing else about the frame is configurable from outside.
 */
import { computed, useTemplateRef, type ComputedRef } from 'vue'

/** Props accepted by {@link UiSandboxFrame}. */
const props = withDefaults(
  defineProps<{
    /** The frame document's address. */
    src: string
    /** The frame's accessible name, already translated by the caller. */
    title: string
    /** The frame's height in CSS pixels; the caller tracks what the framed screen asks for. */
    height?: number
  }>(),
  { height: 320 },
)

/** Events emitted by {@link UiSandboxFrame}. */
const emit = defineEmits<{
  /** The frame document finished loading. */
  (e: 'load'): void
  /** The frame could not be loaded. */
  (e: 'error'): void
}>()

/** The `<iframe>` element. */
const element = useTemplateRef<HTMLIFrameElement>('frame')

/** The inline height, in pixels. */
const heightStyle: ComputedRef<string> = computed(() => {
  return `${props.height.toString()}px`
})

/**
 * Forwards the iframe's load event.
 * @returns Nothing.
 */
const onLoad = (): void => {
  emit('load')
}

/**
 * Forwards the iframe's error event.
 * @returns Nothing.
 */
const onError = (): void => {
  emit('error')
}

defineExpose({ element })
</script>

<template>
  <iframe
    ref="frame"
    :src="src"
    :title="title"
    :style="{ height: heightStyle }"
    sandbox="allow-scripts"
    referrerpolicy="no-referrer"
    loading="eager"
    class="block w-full min-w-0 rounded-control border-0 bg-surface-1"
    @load="onLoad"
    @error="onError"
  />
</template>
