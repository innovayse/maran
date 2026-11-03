<script setup lang="ts">
/**
 * The dialog an owner changes one sub-user's features in: the same checkboxes as the invite form, over
 * the person's current set. Dumb: the page holds which sub-user is open and carries the change out on
 * `save`; the dialog keeps only the ticks in progress, and starts them over from the person's current
 * set each time it opens.
 */
import { ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiModal from '../ui/UiModal.vue'
import SubUserFeaturePicker from './SubUserFeaturePicker.vue'
import type { AccountFeatureName } from '../../types/accountFeature'
import type { GrantableFeature, SubUser } from '../../types/subUser'

/** Props accepted by {@link SubUserFeaturesDialog}. */
const props = defineProps<{
  /** The sub-user being changed, or `null` while none is (the dialog is closed). */
  subUser: SubUser | null
  /** Every feature the owner may give, from the panel. */
  features: GrantableFeature[]
  /** Whether the save request is in flight, which disables the save control. */
  acting: boolean
}>()

/** Events emitted by {@link SubUserFeaturesDialog}. */
const emit = defineEmits<{
  /** The owner abandoned the change. */
  (e: 'close'): void
  /** The owner saved; carries the complete new set of feature ids. */
  (e: 'save', features: AccountFeatureName[]): void
}>()

const { t } = useI18n()

/** The ticks in progress. */
const selected: Ref<AccountFeatureName[]> = ref([])

/**
 * Reports the ticks as the new set.
 * @returns Nothing; emits synchronously.
 */
const save = (): void => {
  emit('save', selected.value)
}

// Start from what the person holds each time the dialog opens on someone, so an abandoned edit never
// shows up as the next person's starting point.
watch(
  (): SubUser | null => {
    return props.subUser
  },
  (subUser: SubUser | null): void => {
    selected.value = subUser === null ? [] : [...subUser.features]
  },
  { immediate: true },
)
</script>

<template>
  <UiModal
    :open="subUser !== null"
    :title="t('subUsers.featuresDialog.title', { name: subUser?.username ?? '' })"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
    <p class="mb-3 text-sm text-text-secondary">{{ t('subUsers.featuresDialog.description') }}</p>
    <SubUserFeaturePicker
      v-model="selected"
      :features="features"
      :group-label="t('subUsers.features.groupLabel')"
    />
    <template #footer>
      <UiButton variant="secondary" @click="emit('close')">{{ t('common.cancel') }}</UiButton>
      <UiButton :disabled="acting" @click="save">{{ t('subUsers.featuresDialog.save') }}</UiButton>
    </template>
  </UiModal>
</template>
