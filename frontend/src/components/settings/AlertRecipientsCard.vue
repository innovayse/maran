<script setup lang="ts">
/**
 * The list of addresses alert mail is sent to, with an editor: add one address, remove any, save the
 * whole list. An empty list is allowed and stops alert mail. The list is edited as a draft and sent
 * in one request, because the server replaces the stored list wholesale.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiForm from '../ui/UiForm.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiInput from '../ui/UiInput.vue'
import UiLoadingRegion from '../ui/UiLoadingRegion.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { useAlertRecipientsStore } from '../../stores/alertRecipients'
import { alertRecipientProblem } from '../../utils/alertRecipientProblem'

const { t } = useI18n()
const store = useAlertRecipientsStore()

/** The draft list, copied from the panel's and edited here until saved. */
const draft: Ref<string[]> = ref([])

/** The address being typed into the add field. */
const candidate: Ref<string> = ref('')

/** The sentence under the add field when the typed address was refused, or `null`. */
const candidateError: Ref<string | null> = ref(null)

/** The sentence under the title naming the cap, or `undefined` until the panel has reported it. */
const description: ComputedRef<string | undefined> = computed(() => {
  return store.recipients === null
    ? undefined
    : t('alertSettings.recipients.description', { max: store.recipients.maximum })
})

/** True while the list is at its cap and no further address can be added. */
const full: ComputedRef<boolean> = computed(() => {
  return store.recipients !== null && draft.value.length >= store.recipients.maximum
})

/**
 * Replaces the draft with the list the panel reported.
 * @returns Nothing; the draft is updated synchronously.
 */
const fill = (): void => {
  draft.value = store.recipients === null ? [] : [...store.recipients.addresses]
}

/**
 * Adds the typed address to the draft after the cheap checks, leaving the field for the next one.
 * @returns Nothing; the draft and the field are updated synchronously.
 */
const add = (): void => {
  if (candidate.value.trim() === '') {
    candidateError.value = null
    return
  }
  if (full.value && store.recipients !== null) {
    candidateError.value = t('alertSettings.recipients.tooMany', { max: store.recipients.maximum })
    return
  }

  const problem = alertRecipientProblem(candidate.value, draft.value)
  if (problem !== null) {
    candidateError.value = t(`alertSettings.recipients.${problem}`)
    return
  }

  draft.value = [...draft.value, candidate.value.trim()]
  candidate.value = ''
  candidateError.value = null
}

/**
 * Removes one address from the draft.
 * @param address The address to drop.
 * @returns Nothing; the draft is updated synchronously.
 */
const remove = (address: string): void => {
  draft.value = draft.value.filter((entry) => {
    return entry !== address
  })
}

/**
 * Sends the draft as the new list. An address left in the add field is taken first, so pressing
 * Save never silently drops what was typed.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  add()
  if (candidateError.value !== null) {
    return
  }

  await store.save({ addresses: draft.value })
}

// The draft follows whatever the panel last reported, including after a save.
watch(
  () => {
    return store.recipients
  },
  fill,
  { immediate: true },
)

void store.load()
</script>

<template>
  <UiCard>
    <UiSectionHeading
      class="mb-4"
      :title="t('alertSettings.recipients.title')"
      :subtitle="description"
    />

    <div class="mb-4 flex flex-col gap-3 empty:hidden">
      <UiAlert v-if="store.submitMessage !== null" variant="error">{{ store.submitMessage }}</UiAlert>
      <UiAlert v-if="store.saved" variant="success">{{ t('alertSettings.recipients.saved') }}</UiAlert>
    </div>

    <UiLoadingRegion :state="store.loadState">
      <template #error>
        <UiAlert variant="error">{{ store.loadMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>
      <UiForm :loading="store.loadState === 'loading'" @submit="submit">
        <div class="flex flex-col gap-3">
          <UiFieldHint v-if="draft.length === 0">{{ t('alertSettings.recipients.empty') }}</UiFieldHint>
          <ul v-else class="flex flex-col gap-2" :aria-label="t('alertSettings.recipients.listLabel')">
            <li v-for="address in draft" :key="address" class="flex items-center justify-between gap-3">
              <span class="min-w-0 font-mono text-sm wrap-anywhere">{{ address }}</span>
              <UiButton
                variant="ghost"
                size="icon-sm"
                :aria-label="t('alertSettings.recipients.remove', { address })"
                @click="remove(address)"
              >
                <UiIcon name="x" size="sm" />
              </UiButton>
            </li>
          </ul>

          <div class="flex items-end gap-3">
            <div class="min-w-0 grow">
              <UiInput
                v-model="candidate"
                :label="t('alertSettings.recipients.addLabel')"
                :error="candidateError"
                type="email"
                inputmode="email"
                mono
                @keydown.enter.prevent="add"
              />
            </div>
            <UiButton variant="secondary" :disabled="full" @click="add">
              {{ t('alertSettings.recipients.add') }}
            </UiButton>
          </div>

          <div>
            <UiButton type="submit" :disabled="store.saving">{{ t('alertSettings.recipients.save') }}</UiButton>
          </div>
        </div>
      </UiForm>
    </UiLoadingRegion>
  </UiCard>
</template>
