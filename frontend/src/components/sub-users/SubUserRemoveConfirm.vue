<script setup lang="ts">
/**
 * Asks the owner to confirm removing one sub-user before the page sends it, naming the person and the
 * consequence. Dumb: the page holds which sub-user is pending and carries the removal out on `confirm`.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiConfirm from '../ui/UiConfirm.vue'
import type { SubUser } from '../../types/subUser'

/** Props accepted by {@link SubUserRemoveConfirm}. */
const props = defineProps<{
  /** The sub-user awaiting confirmation, or `null` while none is (the dialog is closed). */
  subUser: SubUser | null
  /** Whether the confirmed request is in flight, which holds the dialog's spinner. */
  acting: boolean
}>()

/** Events emitted by {@link SubUserRemoveConfirm}. */
const emit = defineEmits<{
  /** The owner abandoned the removal. */
  (e: 'close'): void
  /** The owner confirmed the removal. */
  (e: 'confirm'): void
}>()

const { t } = useI18n()

/** The confirmation's title, naming the person. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  return t('subUsers.remove.title', { name: props.subUser?.username ?? '' })
})
</script>

<template>
  <UiConfirm
    :open="subUser !== null"
    :title="confirmationTitle"
    :question="t('subUsers.remove.question')"
    :confirm-label="t('subUsers.remove.confirm')"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="acting"
    :acting-label="t('subUsers.working')"
    @close="emit('close')"
    @confirm="emit('confirm')"
  />
</template>
