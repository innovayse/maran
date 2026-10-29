<script setup lang="ts">
/**
 * First-run setup: the one screen a panel with no administrator will show, for
 * whatever route is asked for. The token is typed in, never read out of the
 * address: the installer prints the link and the token as two separate lines
 * (installer/lib/90-finish.sh) because a secret in a web address is written to
 * the panel's own nginx logs and to the browser's history, neither of which this
 * product can unwrite
 * (docs/superpowers/notes/2026-09-11-setup-token-in-a-url-threat-note.md).
 *
 * A visitor who arrives with `?token=` anyway — an old bookmark, or the two
 * printed lines pasted back into one address — is told so and asked to paste the
 * token into the field, and the address is cleared. Honouring the value would
 * put the secret back in the log the fix took it out of; ignoring it silently
 * would leave them looking at an empty field they believe was filled.
 *
 * The strength meter is advice. The server's validator is the authority, and a
 * disagreement between them is a bug in this page, never a reason to submit.
 *
 * The auth layout owns the mark and the wordmark and gives its pages no `h1`, so the title block
 * ({@link UiAuthHeading}, an `h2`) is the screen's title.
 * The strength note is a polite live region WITHOUT `role="status"`: the token notice is found
 * by that role, and a second status on the screen would make it ambiguous.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiAuthHeading from '../../components/ui/UiAuthHeading.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiPasswordInput from '../../components/ui/UiPasswordInput.vue'
import { useAuthStore } from '../../stores/auth'

/** The shortest password the backend accepts. Mirrors its validator, and is only a hint. */
const MIN_PASSWORD_LENGTH = 12

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

/** The one-time token. Always typed or pasted by the operator; never prefilled. */
const token: Ref<string> = ref('')

/**
 * Whether this visit arrived with a token in the address. Drives the notice that
 * explains why the field below is empty; the value itself is discarded.
 */
const arrivedWithATokenInTheAddress: Ref<boolean> = ref(false)

/** The administrator's login name. */
const username: Ref<string> = ref('')

/** The administrator's contact address. */
const email: Ref<string> = ref('')

/** The chosen password. */
const password: Ref<string> = ref('')

/** The password typed a second time, so a typo does not become the only way in. */
const confirmation: Ref<string> = ref('')

/** Whether the password is long enough for the backend to accept it. */
const isLongEnough: ComputedRef<boolean> = computed(() => {
  return password.value.length >= MIN_PASSWORD_LENGTH
})

/** How strong the password reads, which only picks the label and its colour. */
const strength: ComputedRef<'none' | 'weak' | 'fair' | 'strong'> = computed(() => {
  if (password.value.length === 0) {
    return 'none'
  }

  if (!isLongEnough.value) {
    return 'weak'
  }

  return password.value.length >= 20 ? 'strong' : 'fair'
})

/** Text colour of the strength note: amber while short, green once strong, quiet in between. */
const strengthClasses: ComputedRef<string> = computed(() => {
  switch (strength.value) {
    case 'weak':
      return 'text-warning'
    case 'strong':
      return 'text-success'
    default:
      return 'text-text-secondary'
  }
})

/** The advisory strength label shown under the password field. */
const strengthLabel: ComputedRef<string> = computed(() => {
  switch (strength.value) {
    case 'weak':
      return t('app.auth.passwordStrengthWeak')
    case 'fair':
      return t('app.auth.passwordStrengthFair')
    case 'strong':
      return t('app.auth.passwordStrengthStrong')
    default:
      return ''
  }
})

/** The mismatch message shown under the confirmation field, or null while it matches. */
const confirmationError: ComputedRef<string | null> = computed(() => {
  return confirmation.value.length > 0 && confirmation.value !== password.value
    ? t('app.auth.passwordsDoNotMatch')
    : null
})

/**
 * Creates the administrator and sends the operator to the sign-in screen.
 * @returns Resolves once the attempt has settled.
 */
const submit = async (): Promise<void> => {
  if (confirmationError.value !== null) {
    return
  }

  const created = await authStore.completeSetup({
    token: token.value,
    username: username.value,
    email: email.value,
    password: password.value,
  })

  if (created) {
    // Deliberately not signed in automatically: the operator has just chosen a
    // password, and typing it once proves it is the one they meant to set.
    await router.replace({ name: 'login' })
  }
}

onMounted(() => {
  // Read to decide what to SAY, never to fill the field in. A repeated key arrives as an
  // array, which is still a token in the address, so the notice covers that shape too.
  const inTheAddress = route.query.token
  arrivedWithATokenInTheAddress.value =
    typeof inTheAddress === 'string' ? inTheAddress.length > 0 : Array.isArray(inTheAddress)

  if (arrivedWithATokenInTheAddress.value) {
    // The history entry is already written and nothing here can unwrite it. Clearing the
    // query stops the secret being re-copied out of the address bar, re-shared, or sent to
    // this host again on a reload — where the panel's own web server would log the request.
    void router.replace({ path: route.path })
  }
})
</script>

<template>
  <UiForm @submit="submit">
    <UiAuthHeading class="mb-6" :title="t('app.auth.setupTitle')" :subtitle="t('app.auth.setupSubtitle')" />

    <!-- Tells a visitor with an old bookmark why the token field is empty; the value was dropped. -->
    <UiAlert v-if="arrivedWithATokenInTheAddress" class="mb-5">
      {{ t('app.auth.setupTokenNotInTheLink') }}
    </UiAlert>

    <!-- The server's own wording, verbatim: it names the rule a password broke. -->
    <UiAlert v-if="authStore.errorMessage !== null" variant="error" class="mb-5">
      {{ authStore.errorMessage }}
    </UiAlert>

    <div class="flex flex-col gap-5">
      <UiInput
        v-model="token"
        :label="t('app.auth.setupTokenLabel')"
        icon="keyRound"
        :placeholder="t('app.auth.setupTokenPlaceholder')"
        required
      />

      <!-- The one-time token proves the operator holds the installer's output; everything below
           it describes the administrator being created. -->
      <div class="flex flex-col gap-5 border-t border-border-subtle pt-5">
        <UiInput
          v-model="username"
          :label="t('app.auth.usernameLabel')"
          icon="user"
          :placeholder="t('app.auth.usernamePlaceholder')"
          required
          autocomplete="username"
        />

        <UiInput
          v-model="email"
          type="email"
          :label="t('app.auth.emailLabel')"
          icon="mail"
          :placeholder="t('app.auth.emailPlaceholder')"
          required
          autocomplete="email"
        />

        <div class="flex flex-col gap-2">
          <UiPasswordInput
            v-model="password"
            :label="t('app.auth.passwordLabel')"
            icon="lock"
            :placeholder="t('app.auth.passwordPlaceholder')"
            required
            generate
            autocomplete="new-password"
          />
          <p v-if="strengthLabel !== ''" class="text-sm" :class="strengthClasses" aria-live="polite">
            {{ strengthLabel }}
          </p>
        </div>

        <UiPasswordInput
          v-model="confirmation"
          :label="t('app.auth.confirmPasswordLabel')"
          icon="lock"
          :placeholder="t('app.auth.passwordPlaceholder')"
          :error="confirmationError"
          required
          autocomplete="new-password"
        />
      </div>

      <!-- The one primary pill on the screen, full width with the arrow after the text. The arrow
           is decorative; the text is the name. -->
      <UiButton class="w-full" type="submit" :disabled="authStore.loading">
        {{ authStore.loading ? t('app.auth.creating') : t('app.auth.createAdministrator') }}
        <UiIcon name="arrowRight" size="sm" />
      </UiButton>
    </div>
  </UiForm>
</template>
