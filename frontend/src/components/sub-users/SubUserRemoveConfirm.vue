<script setup lang="ts">
/**
 * Asks the owner to confirm removing one sub-user (or cancelling a pending offer) before the page sends it, naming the person and the
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

/** Whether the row is a pending offer: cancelling it says nothing of a deleted login. */
const isOffer: ComputedRef<boolean> = computed(() => {
  return props.subUser?.isOffer === true
})

/** The confirmation's title, naming the person. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const name = props.subUser?.username ?? ''
  return isOffer.value ? t('subUsers.cancelOffer.title', { name }) : t('subUsers.remove.title', { name })
})

/** The confirmation's question. */
const confirmationQuestion: ComputedRef<string> = computed(() => {
  return isOffer.value ? t('subUsers.cancelOffer.question') : t('subUsers.remove.question')
})

/** The confirm button's label. */
const confirmationLabel: ComputedRef<string> = computed(() => {
  return isOffer.value ? t('subUsers.cancelOffer.confirm') : t('subUsers.remove.confirm')
})
</script>

<template>
  <UiConfirm
    :open="subUser !== null"
    :title="confirmationTitle"
    :question="confirmationQuestion"
    :confirm-label="confirmationLabel"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="acting"
    :acting-label="t('subUsers.working')"
    @close="emit('close')"
    @confirm="emit('confirm')"
  />
</template>
