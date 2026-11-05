<script setup lang="ts">
/**
 * Asks the administrator to confirm revoking one key before the page sends it, naming the key and
 * the consequence. Dumb: the page holds which key is pending and carries the revoke out on `confirm`.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiConfirm from '../ui/UiConfirm.vue'
import type { ApiKey } from '../../types/apiKey'

/** Props accepted by {@link ApiKeyRevokeConfirm}; the page mounts it only while a revoke waits. */
const props = defineProps<{
  /** The key awaiting confirmation. */
  pending: ApiKey
  /** Whether the confirmed request is in flight, which holds the dialog's spinner. */
  acting: boolean
}>()

/** Events emitted by {@link ApiKeyRevokeConfirm}. */
const emit = defineEmits<{
  /** The administrator abandoned the revoke. */
  (e: 'close'): void
  /** The administrator confirmed the revoke. */
  (e: 'confirm'): void
}>()

const { t } = useI18n()

/** The confirmation's title, naming the key. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  return t('apiKeys.confirmRevokeTitle', { name: props.pending.name })
})
</script>

<template>
  <UiConfirm
    :open="true"
    :title="confirmationTitle"
    :question="t('apiKeys.confirmRevoke')"
    :confirm-label="t('apiKeys.confirmRevokeAction')"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="acting"
    :acting-label="t('apiKeys.working')"
    destructive
    @close="emit('close')"
    @confirm="emit('confirm')"
  />
</template>
