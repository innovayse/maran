<script setup lang="ts">
/**
 * What a packaged module's screen shows when its frame was dropped: the module did not start, could
 * not be loaded, or was stopped for sending messages the panel does not accept. It is a state of
 * the module's screen, never of the panel: the shell, the menu and every other page keep working,
 * and the sentence says so. A retry mounts a fresh frame with a fresh handshake.
 *
 * The explanation is the panel's own chrome text, chosen by the reason; nothing the module sent
 * is shown here, because the module is the party that just misbehaved.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiEmptyState from '../ui/UiEmptyState.vue'
import type { ModuleFailureReason } from '../../types/moduleBridge'

/** Props accepted by {@link ModuleUnavailable}. */
const props = defineProps<{
  /** Why the frame was dropped. */
  reason: ModuleFailureReason
  /** The module's name as the panel reports it, already localized. */
  moduleName: string
}>()

/** Events emitted by {@link ModuleUnavailable}. */
const emit = defineEmits<{
  /** The person asked to start the module's screen again. */
  (e: 'retry'): void
}>()

/** The locale key of the sentence each reason is explained by. */
const REASON_KEYS: Readonly<Record<ModuleFailureReason, string>> = {
  timeout: 'app.modules.unavailable.timeout',
  'load-error': 'app.modules.unavailable.loadError',
  violations: 'app.modules.unavailable.violations',
}

const { t } = useI18n()

/** The sentence for the reason. */
const description: ComputedRef<string> = computed(() => {
  return t(REASON_KEYS[props.reason])
})

/**
 * Asks the screen to mount the frame again.
 * @returns Nothing.
 */
const retry = (): void => {
  emit('retry')
}
</script>

<template>
  <UiEmptyState
    :title="t('app.modules.unavailable.title', { module: moduleName })"
    :description="`${description} ${t('app.modules.unavailable.hint')}`"
  >
    <UiButton variant="secondary" @click="retry">{{ t('app.modules.retry') }}</UiButton>
  </UiEmptyState>
</template>
