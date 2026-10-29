<script setup lang="ts">
/**
 * Asks the panel to send a password-reset link.
 *
 * **The screen after the request is the same screen for every address**, and that
 * is the whole design of this page. The backend answers identically whether or not
 * an address belongs to an account, and takes deliberate care to spend the same
 * time on both; a UI that distinguished them — by wording, by layout, by a spinner
 * that only ran for one of them — would hand back the account-enumeration oracle
 * that effort exists to close. So there is exactly one confirmation rendering here,
 * reached by exactly one branch, and the store's action returns nothing for this
 * page to branch on.
 *
 * The confirmation says a link has been sent *if the address belongs to an
 * account*, because that is the only true statement the panel can make without
 * telling the reader which case they are in.
 */
import { ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiAuthHeading from '../../components/ui/UiAuthHeading.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiInput from '../../components/ui/UiInput.vue'
import { useAuthStore } from '../../stores/auth'

const { t } = useI18n()
const authStore = useAuthStore()

/** The address being typed. */
const email: Ref<string> = ref('')

/** True once a request has been made and the panel answered it. */
const submitted: Ref<boolean> = ref(false)

/**
 * Asks for the link, then shows the one confirmation this page has.
 *
 * Nothing is inspected: the action reports no outcome, so there is no value here
 * that could be used to render one address differently from another.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  await authStore.requestPasswordReset(email.value)

  // A panel that could not be reached is a different fact from anything about the
  // address, and it is the only condition that keeps the form on screen.
  if (authStore.errorMessage === null) {
    submitted.value = true
  }
}
</script>

<template>
  <div>
    <UiAuthHeading
      class="mb-6"
      :title="t('app.passwordReset.requestTitle')"
      :subtitle="t('app.passwordReset.requestSubtitle')"
    />

    <!-- One confirmation, no alternative. There is no `v-else-if` on this branch and
         there must never be one: a second rendering is a second answer, and the two
         would differ by exactly the fact the backend refuses to disclose. -->
    <div v-if="submitted" class="flex flex-col gap-5">
      <UiAlert variant="info">{{ t('app.passwordReset.requestSent') }}</UiAlert>
      <UiButton variant="secondary" class="w-full justify-center" :to="{ name: 'login' }">
        {{ t('app.passwordReset.backToSignIn') }}
      </UiButton>
    </div>

    <template v-else>
      <UiAlert v-if="authStore.errorMessage !== null" variant="error" class="mb-5">
        {{ authStore.errorMessage }}
      </UiAlert>

      <UiForm @submit="submit">
        <div class="flex flex-col gap-5">
          <UiInput
            v-model="email"
            :label="t('app.auth.emailLabel')"
            icon="mail"
            :placeholder="t('app.auth.emailPlaceholder')"
            required
            autocomplete="email"
          />

          <!-- The one primary pill on the screen, full width, with the arrow after its text. -->
          <UiButton class="w-full justify-center gap-2" type="submit" :disabled="authStore.loading">
            {{ t('app.passwordReset.sendLink') }}
            <UiIcon name="arrowRight" />
          </UiButton>

          <UiButton variant="ghost" class="justify-center" :to="{ name: 'login' }">
            {{ t('app.passwordReset.backToSignIn') }}
          </UiButton>
        </div>
      </UiForm>
    </template>
  </div>
</template>
