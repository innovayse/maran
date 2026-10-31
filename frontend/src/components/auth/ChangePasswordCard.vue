<script setup lang="ts">
/**
 * The signed-in person's own password change, on `/profile`, for every role.
 *
 * Three fields: the current password, the new one, and the new one again. The only
 * check made here is that the two new entries match — a fact about this form, not
 * about the panel. Everything else (whether the current password is right, the
 * policy's minimum length) is the backend's to judge, and its already-localized
 * refusal is shown as sent; a client-side length rule would be a second copy of an
 * operator setting that can change without this bundle changing (rules/vue.md
 * "Forms: the browser never validates").
 *
 * Every other sign-in ends on success and this one stays, which the success message
 * says, so nobody is surprised when another device asks them to sign in again.
 *
 * State lives in `stores/profile.ts`; this component never calls an API composable.
 */
import { ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useProfileStore } from '../../stores/profile'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiForm from '../ui/UiForm.vue'
import UiPasswordInput from '../ui/UiPasswordInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'

const { t } = useI18n()
const store = useProfileStore()

/** The password the caller signs in with today. */
const currentPassword: Ref<string> = ref('')

/** The password being set. */
const newPassword: Ref<string> = ref('')

/** The new password typed again, so a typo does not become the new password. */
const confirmation: Ref<string> = ref('')

/** The panel's own message when the two new entries differ, or `null` while they match. */
const mismatch: Ref<string | null> = ref(null)

/** Where the last attempt ended: not yet tried, changed, or refused. */
const outcome: Ref<'idle' | 'changed' | 'refused'> = ref('idle')

/**
 * Submits the change.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  if (newPassword.value !== confirmation.value) {
    mismatch.value = t('app.auth.passwordsDoNotMatch')
    return
  }

  mismatch.value = null
  const changed = await store.changePassword({
    currentPassword: currentPassword.value,
    newPassword: newPassword.value,
  })

  outcome.value = changed ? 'changed' : 'refused'
  if (changed) {
    // A password left in a field after it was accepted is a secret on screen for no reason.
    currentPassword.value = ''
    newPassword.value = ''
    confirmation.value = ''
  }
}
</script>

<template>
  <UiCard>
    <UiSectionHeading
      class="mb-4"
      :title="t('app.profile.passwordHeading')"
      :subtitle="t('app.profile.passwordDescription')"
    />

    <UiAlert v-if="outcome === 'changed'" variant="success" class="mb-4">
      {{ t('app.profile.passwordChanged') }}
    </UiAlert>
    <!-- The refusal as the backend worded it; a panel that could not be reached has no words of
         its own to send, so the SPA's one unreachable message stands in. -->
    <UiAlert v-else-if="outcome === 'refused'" variant="error" class="mb-4">
      {{ store.passwordErrorMessage ?? t('app.status.unreachable') }}
    </UiAlert>

    <UiForm @submit="submit">
      <div class="flex max-w-md flex-col gap-4">
        <UiPasswordInput
          v-model="currentPassword"
          :label="t('app.profile.currentPasswordLabel')"
          icon="lock"
          required
          autocomplete="current-password"
        />
        <UiPasswordInput
          v-model="newPassword"
          :label="t('app.profile.newPasswordLabel')"
          icon="lock"
          required
          autocomplete="new-password"
          generate
        />
        <UiPasswordInput
          v-model="confirmation"
          :label="t('app.auth.confirmPasswordLabel')"
          icon="lock"
          :error="mismatch"
          required
          autocomplete="new-password"
        />
        <div>
          <UiButton type="submit" :disabled="store.passwordSaving">
            {{ t('app.profile.changePassword') }}
          </UiButton>
        </div>
      </div>
    </UiForm>
  </UiCard>
</template>
