<script setup lang="ts">
/**
 * Asks the administrator to confirm the removal of one queued message before the page sends it,
 * naming the message and the consequence: it is deleted from the queue and never delivered. Dumb:
 * the page holds which message is pending and carries the removal out on `confirm`.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiConfirm from '../ui/UiConfirm.vue'
import type { MailQueueEntry } from '../../types/mailQueue'

/** Props accepted by {@link MailQueueRemoveConfirm}; the page mounts it only while a removal waits. */
const props = defineProps<{
  /** The message awaiting removal. */
  entry: MailQueueEntry
  /** Whether the confirmed request is in flight, which holds the dialog's spinner. */
  acting: boolean
}>()

/** Events emitted by {@link MailQueueRemoveConfirm}. */
const emit = defineEmits<{
  /** The administrator abandoned the removal. */
  (e: 'close'): void
  /** The administrator confirmed the removal. */
  (e: 'confirm'): void
}>()

const { t } = useI18n()

/** The confirmation's title, naming the message by its queue id. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  return t('mail.queue.confirmRemoveTitle', { id: props.entry.queueId })
})
</script>

<template>
  <UiConfirm
    :open="true"
    :title="confirmationTitle"
    :question="t('mail.queue.confirmRemove')"
    :confirm-label="t('mail.queue.confirmRemoveAction')"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="acting"
    :acting-label="t('mail.working')"
    destructive
    @close="emit('close')"
    @confirm="emit('confirm')"
  />
</template>
