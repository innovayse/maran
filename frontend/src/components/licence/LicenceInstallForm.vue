<script setup lang="ts">
/**
 * The form that installs a pasted licence: one text area and the submit button.
 *
 * The licence text is a long JSON blob. It lives only in this component's local state, goes to the
 * store on submit, and is cleared as soon as the panel accepted it; it is rendered nowhere except
 * inside the text area (never as markup) and is never logged. A refusal keeps the text so the
 * operator can correct it, and shows the panel's own sentence. Editing the text drops the outcome
 * of the previous attempt, since it no longer describes what is in the field.
 */
import { ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiForm from '../ui/UiForm.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiTextarea from '../ui/UiTextarea.vue'
import { useLicenceStore } from '../../stores/licence'

const { t } = useI18n()
const licenceStore = useLicenceStore()

/** The licence text the operator pasted. */
const rawLicenceText: Ref<string> = ref('')

/** Whether a submit was tried, so the "required" message appears only after one. */
const submitted: Ref<boolean> = ref(false)

/**
 * Sends the pasted text to the store and forgets it once the panel accepted it.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  submitted.value = true
  if (rawLicenceText.value.trim().length === 0) {
    return
  }
  const accepted = await licenceStore.install(rawLicenceText.value)
  if (accepted) {
    rawLicenceText.value = ''
    submitted.value = false
  }
}

watch(rawLicenceText, (value) => {
  // An edit makes the last outcome stale: a refusal or a success no longer describes the field.
  // Emptying the field is not an edit: it is how a successful install clears the text, and it
  // must not wipe the success message in the same tick.
  if (value !== '') {
    licenceStore.resetInstall()
  }
})
</script>

<template>
  <UiCard>
    <UiForm @submit="submit">
      <div class="flex flex-col gap-4">
        <UiSectionHeading :title="t('licence.installTitle')" />

        <UiAlert v-if="licenceStore.installMessage !== null" variant="error">
          {{ licenceStore.installMessage }}
        </UiAlert>
        <UiAlert v-else-if="licenceStore.installFailed" variant="error">
          {{ t('app.status.unreachable') }}
        </UiAlert>
        <UiAlert v-if="licenceStore.installed" variant="success" data-testid="licence-installed">
          {{ t('licence.installed') }}
        </UiAlert>

        <div>
          <UiTextarea
            v-model="rawLicenceText"
            :label="t('licence.installLabel')"
            :error="submitted && rawLicenceText.trim().length === 0 ? t('licence.installRequired') : null"
            :rows="8"
            mono
            no-wrap
            required
          />
          <UiFieldHint class="mt-1">{{ t('licence.installHint') }}</UiFieldHint>
        </div>

        <div class="flex flex-wrap justify-end gap-2">
          <UiButton type="submit" :disabled="licenceStore.installing">
            {{ licenceStore.installing ? t('licence.installing') : t('licence.installSubmit') }}
          </UiButton>
        </div>
      </div>
    </UiForm>
  </UiCard>
</template>
