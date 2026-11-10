<script setup lang="ts">
/**
 * The confirmation for removing an installed module. It says plainly what a removal does and does
 * not do: the module stops after the next restart of the panel, and its data is kept, so installing
 * it again brings it back. There is no option to delete the data here by design; the panel offers
 * none. The administrator confirms with their password, which {@link PasswordConfirmDialog} collects.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import PasswordConfirmDialog from './PasswordConfirmDialog.vue'

/** Props accepted by {@link RemoveModuleDialog}. */
const props = defineProps<{
  /** Whether the dialog is shown; owned by the caller. */
  open: boolean
  /** The module's name, localized by the panel. */
  moduleName: string
  /** Whether the request is in flight. */
  acting: boolean
  /** The panel's sentence for the last refusal, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link RemoveModuleDialog}. */
const emit = defineEmits<{
  /** The administrator declined. */
  (e: 'close'): void
  /** The administrator confirmed; carries the typed password. */
  (e: 'submit', password: string): void
}>()

const { t } = useI18n()

/** The dialog's heading, naming the module. */
const title: ComputedRef<string> = computed(() => {
  return t('marketplace.remove.title', { name: props.moduleName })
})
</script>

<template>
  <PasswordConfirmDialog
    :open="open"
    :title="title"
    :question="t('marketplace.remove.question')"
    :submit-label="t('marketplace.remove.submit')"
    :acting="acting"
    :acting-label="t('marketplace.switch.working')"
    :error-message="errorMessage"
    destructive
    @close="emit('close')"
    @submit="emit('submit', $event)"
  />
</template>
