<script setup lang="ts">
/**
 * The notice for a module that is installed (staged) and starts after the panel restarts. Install,
 * update, removal and rollback all end here, because the panel loads modules at start and never
 * swaps one in a running process. The notice says so and offers "Restart the panel", which asks for
 * confirmation (the panel is unavailable for a short time) before it emits `restart`.
 *
 * While the restart is under way (`restarting`) the notice becomes a polite status line saying the
 * page is waiting for the module; the caller polls the LOCAL panel with the store's
 * `waitForModule` and prints the outcome itself. Whether a restart is possible at all is the
 * panel's answer to the request, so the button is never hidden by a guess.
 */
import { ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiConfirm from '../ui/UiConfirm.vue'

/** Props accepted by {@link RestartRequiredNotice}. */
defineProps<{
  /** The module waiting for the restart, as its name is printed. */
  moduleName: string
  /** True while the restart request is in flight. */
  busy: boolean
  /** True from an accepted restart until the wait for the module ended. */
  restarting: boolean
}>()

/** Events emitted by {@link RestartRequiredNotice}. */
const emit = defineEmits<{
  /** The administrator confirmed the restart. */
  (e: 'restart'): void
}>()

const { t } = useI18n()

/** Whether the confirmation dialog is open. */
const confirming: Ref<boolean> = ref(false)

/**
 * Opens the confirmation dialog.
 * @returns Nothing.
 */
const ask = (): void => {
  confirming.value = true
}

/**
 * Closes the dialog without restarting.
 * @returns Nothing.
 */
const dismiss = (): void => {
  confirming.value = false
}

/**
 * Closes the dialog and reports the confirmed restart.
 * @returns Nothing.
 */
const confirm = (): void => {
  confirming.value = false
  emit('restart')
}
</script>

<template>
  <UiAlert v-if="restarting" variant="info" data-testid="restart-in-progress">
    <p class="font-medium">{{ t('marketplace.restart.restarting') }}</p>
    <p>{{ t('marketplace.restart.restartingDescription', { name: moduleName }) }}</p>
  </UiAlert>
  <UiAlert v-else variant="warning" data-testid="restart-required">
    <p class="font-medium">{{ t('marketplace.restart.title') }}</p>
    <p>{{ t('marketplace.restart.description', { name: moduleName }) }}</p>
    <template #actions>
      <UiButton :disabled="busy" @click="ask">{{ t('marketplace.restart.action') }}</UiButton>
    </template>
  </UiAlert>

  <UiConfirm
    :open="confirming"
    :title="t('marketplace.restart.confirmTitle')"
    :question="t('marketplace.restart.confirmQuestion')"
    :confirm-label="t('marketplace.restart.confirmAction')"
    :cancel-label="t('marketplace.cancel')"
    :close-label="t('marketplace.close')"
    :acting-label="t('marketplace.restart.restarting')"
    :destructive="false"
    @close="dismiss"
    @confirm="confirm"
  />
</template>
