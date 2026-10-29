<script setup lang="ts">
/**
 * Sets the first password for an invited customer login, from
 * `/accept-invitation?token=…`.
 *
 * Modeled directly on {@link ../auth/ResetPasswordPage.vue}: same form shape, same
 * password rules, same error rendering — only the endpoint and the copy differ.
 * The token comes from the query string, because that is where the invitation
 * mail put it, and it is the only credential this screen has — nothing here
 * names the account, and naming one would let a caller aim a token at somebody
 * else's login.
 *
 * The new-password field offers to generate one, exactly as the reset screen's
 * does: a secret is being minted here rather than an existing one entered, and
 * generating also reveals the value so it can be recorded.
 *
 * A link carrying no token is a stated state of its own — a warning and the way
 * back, with no form, because that request is certain to be refused.
 *
 * A refusal is rendered exactly as the panel sent it, beside a way back to the
 * sign-in screen and one fixed line saying what to do next (ask for a new
 * invitation). The backend answers a token that never existed, one that has
 * expired and one already spent with the same message, and nothing on this page
 * inspects it: a screen that told them apart would be the disclosure the single
 * message exists to prevent.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiAuthHeading from '../../components/ui/UiAuthHeading.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiPasswordInput from '../../components/ui/UiPasswordInput.vue'
import { useAuthStore } from '../../stores/auth'

const { t } = useI18n()
const route = useRoute()
const authStore = useAuthStore()

/** The password being set. */
const password: Ref<string> = ref('')

/** The same password typed again, so a typo does not become the new password. */
const confirmation: Ref<string> = ref('')

/** The panel's own message when the two entries differ, or `null` while they match. */
const mismatch: Ref<string | null> = ref(null)

/** True once the password has been set. */
const done: Ref<boolean> = ref(false)

/** The token from the invitation link, or the empty string when the link carried none. */
const token: ComputedRef<string> = computed(() => {
  return typeof route.query.token === 'string' ? route.query.token : ''
})

/**
 * Sets the first password.
 *
 * The two entries are compared here because that is a fact about this form
 * rather than about the panel; everything else — the token, the length — is the
 * server's to judge, and its already-localized refusal is what the screen shows.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  if (password.value !== confirmation.value) {
    mismatch.value = t('app.auth.passwordsDoNotMatch')
    return
  }

  mismatch.value = null
  done.value = await authStore.acceptInvitation({ token: token.value, newPassword: password.value })
}
</script>

<template>
  <div>
    <UiAuthHeading
      class="mb-6"
      :title="t('app.acceptInvitation.title')"
      :subtitle="t('app.acceptInvitation.subtitle')"
    />

    <!-- Done: the password is set, and the only thing left to do is sign in with it. -->
    <div v-if="done" class="flex flex-col gap-5">
      <UiAlert variant="success">{{ t('app.acceptInvitation.done') }}</UiAlert>
      <UiButton class="w-full justify-center" :to="{ name: 'login' }">
        {{ t('app.acceptInvitation.backToSignIn') }}
        <UiIcon name="arrowRight" size="sm" />
      </UiButton>
    </div>

    <!-- A link that carried no token can never succeed, so the form is not offered: the state is
         stated, with what to do next, instead of a form that is certain to be refused. -->
    <div v-else-if="token === ''" class="flex flex-col gap-5">
      <UiAlert variant="warning">
        <p class="font-semibold">{{ t('app.acceptInvitation.missingTokenTitle') }}</p>
        <p class="mt-1">{{ t('app.acceptInvitation.missingTokenBody') }}</p>
      </UiAlert>
      <UiButton variant="ghost" class="justify-center" :to="{ name: 'login' }">
        {{ t('app.acceptInvitation.backToSignIn') }}
      </UiButton>
    </div>

    <UiForm v-else @submit="submit">
      <div class="flex flex-col gap-5">
        <!-- The refusal as the backend worded it, never rewritten and never told apart. The hint
             beneath it is the same for every refusal, so it says what to do next without
             disclosing which kind this was; the button at the foot is the way back, because a
             dead end here would leave somebody with a stale link and no route to sign-in. -->
        <UiAlert v-if="authStore.errorMessage !== null" variant="error">
          <p>{{ authStore.errorMessage }}</p>
          <p class="mt-1 text-text-secondary">{{ t('app.acceptInvitation.refusedHint') }}</p>
        </UiAlert>

        <UiPasswordInput
          v-model="password"
          :label="t('app.auth.passwordLabel')"
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

        <!-- The one primary pill on the screen; everything else here is quieter than it. -->
        <UiButton class="w-full justify-center" type="submit" :disabled="authStore.loading">
          {{ t('app.acceptInvitation.setPassword') }}
          <UiIcon name="arrowRight" size="sm" />
        </UiButton>
        <UiButton variant="ghost" class="justify-center" :to="{ name: 'login' }">
          {{ t('app.acceptInvitation.backToSignIn') }}
        </UiButton>
      </div>
    </UiForm>
  </div>
</template>
