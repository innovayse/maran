<script setup lang="ts">
/**
 * Asks a signed-in person for the password they will use from now on, when the one they signed in
 * with was chosen by whoever created their account (issue #514).
 *
 * The sign-in succeeded and there is a token, but that token reaches only the password change: the
 * auth guard (`router/authGuard.ts`) holds the person on this screen from every URL, on the bare
 * layout, so no part of the shell opens just to answer 403. Three fields: the password they signed
 * in with, the new one and the new one again. The only check made here is that the two new entries
 * match — a fact about this form. Everything else (whether the current password is right, the
 * policy's minimum length) is the backend's to judge, and its already-localized refusal is shown as
 * sent; a client-side length rule would be a second copy of an operator setting that can change
 * without this bundle changing (rules/vue.md "Forms: the browser never validates").
 *
 * On success the store renews the session, so the token no longer carries the restriction, and the
 * person is taken into the panel. The only other way out is signing out.
 *
 * State lives in `stores/auth.ts`; this page never calls an API composable.
 */
import { ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiAuthHeading from '../../components/ui/UiAuthHeading.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiPasswordInput from '../../components/ui/UiPasswordInput.vue'
import { useAuthStore } from '../../stores/auth'

const { t } = useI18n()
const router = useRouter()
const authStore = useAuthStore()

/** The password the person signed in with, which the backend checks again. */
const currentPassword: Ref<string> = ref('')

/** The password being set. */
const newPassword: Ref<string> = ref('')

/** The new password typed again, so a typo does not become the new password. */
const confirmation: Ref<string> = ref('')

/** The panel's own message when the two new entries differ, or `null` while they match. */
const mismatch: Ref<string | null> = ref(null)

/** True once the panel refused the last attempt, so the alert shows even when it sent no words. */
const refused: Ref<boolean> = ref(false)

/**
 * Submits the change and, once the session is renewed, enters the panel.
 * @returns Resolves once the request and the navigation have settled.
 */
const submit = async (): Promise<void> => {
  if (newPassword.value !== confirmation.value) {
    mismatch.value = t('app.auth.passwordsDoNotMatch')
    return
  }

  mismatch.value = null
  refused.value = false
  const changed = await authStore.changeRequiredPassword({
    currentPassword: currentPassword.value,
    newPassword: newPassword.value,
  })

  if (!changed) {
    // A renewal the panel refused has already cleared the session, and the guard sends the person
    // to sign in; the alert is for the refusals that leave them here.
    refused.value = authStore.isAuthenticated
    return
  }

  // Spent: a password left in a field after it was accepted is a secret on screen for no reason.
  currentPassword.value = ''
  newPassword.value = ''
  confirmation.value = ''
  // The role's own home is chosen by the router (a reseller is sent from the dashboard to their
  // overview), not here.
  await router.replace({ name: 'system-status' })
}

/**
 * Signs out of this device and returns to the sign-in screen: the way out for somebody who cannot
 * or will not choose the password now.
 * @returns Resolves once the request has settled and the navigation is done.
 */
const signOut = async (): Promise<void> => {
  await authStore.logout()
  await router.push({ name: 'login' })
}
</script>

<template>
  <div>
    <UiAuthHeading
      class="mb-6"
      :title="t('app.auth.changePasswordRequired.title')"
      :subtitle="t('app.auth.changePasswordRequired.subtitle')"
    />

    <!-- The refusal as the backend worded it; a panel that could not be reached has no words of its
         own to send, so the SPA's one unreachable message stands in. -->
    <UiAlert v-if="refused" variant="error" class="mb-5">
      {{ authStore.errorMessage ?? t('app.status.unreachable') }}
    </UiAlert>

    <UiForm @submit="submit">
      <div class="flex flex-col gap-5">
        <UiPasswordInput
          v-model="currentPassword"
          :label="t('app.auth.changePasswordRequired.currentPasswordLabel')"
          icon="lock"
          required
          autocomplete="current-password"
        />
        <UiPasswordInput
          v-model="newPassword"
          :label="t('app.auth.changePasswordRequired.newPasswordLabel')"
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

        <!-- The one primary pill on the screen; signing out is quieter than it. -->
        <UiButton class="w-full justify-center" type="submit" :disabled="authStore.loading">
          {{ t('app.auth.changePasswordRequired.submit') }}
          <UiIcon name="arrowRight" size="sm" />
        </UiButton>
        <UiButton variant="ghost" class="justify-center" @click="signOut">
          {{ t('app.auth.signOut') }}
        </UiButton>
      </div>
    </UiForm>
  </div>
</template>
