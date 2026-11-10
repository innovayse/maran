<script setup lang="ts">
/**
 * Asks the administrator to confirm "retry now" before the page sends it: every deferred message is
 * tried again at once. Not destructive — it removes nothing — so the confirm button is the primary
 * one. Dumb: the page mounts it while the question is open and carries the flush out on `confirm`.
 */
import { useI18n } from 'vue-i18n'
import UiConfirm from '../ui/UiConfirm.vue'

/** Props accepted by {@link MailQueueFlushConfirm}; the page mounts it only while the question is open. */
defineProps<{
  /** Whether the confirmed request is in flight, which holds the dialog's spinner. */
  acting: boolean
}>()

/** Events emitted by {@link MailQueueFlushConfirm}. */
const emit = defineEmits<{
  /** The administrator abandoned the flush. */
  (e: 'close'): void
  /** The administrator confirmed the flush. */
  (e: 'confirm'): void
}>()

const { t } = useI18n()
</script>

<template>
  <UiConfirm
    :open="true"
    :title="t('mail.queue.confirmFlushTitle')"
    :question="t('mail.queue.confirmFlush')"
    :confirm-label="t('mail.queue.confirmFlushAction')"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="acting"
    :acting-label="t('mail.working')"
    :destructive="false"
    @close="emit('close')"
    @confirm="emit('confirm')"
  />
</template>
