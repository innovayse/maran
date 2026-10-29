<script setup lang="ts">
/**
 * The panel's outgoing mail settings: where alerts and password-reset links are
 * sent from. Renders a `<section>`, not a `<main>` — the single `<main>` landmark
 * lives in the layout this page is nested under.
 *
 * **The stored password is never on this screen, because it never leaves the
 * server.** The read model has no field for one; what the form shows instead is a
 * line saying a password is saved, beside an empty field that replaces it only if
 * something is typed. The field carries no generate button either: this is somebody
 * else's provider credential being entered, not a secret being minted, and offering
 * to invent one would offer to break the panel's mail.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPasswordInput from '../../components/ui/UiPasswordInput.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSelect, { type SelectOption } from '../../components/ui/UiSelect.vue'
import { useSmtpSettingsStore } from '../../stores/smtpSettings'
import type { SmtpSecurity, SmtpSettings } from '../../types/smtpSettings'

const { t } = useI18n()
const smtpStore = useSmtpSettingsStore()

/** Host name or address of the mail server, as typed. */
const host: Ref<string> = ref('')

/** TCP port, as typed. Text, because the form is `novalidate` and the server bounds it. */
const port: Ref<string> = ref('')

/**
 * How the connection is protected, held as the plain string `UiSelect` binds.
 *
 * Narrowed to the enum's three values on the way out rather than on the way in: the
 * select's `v-model` is a string by contract, and a union-typed ref here would only
 * be a cast wearing a type annotation.
 */
const security: Ref<string> = ref('startTls')

/** The submission user name, as typed. */
const username: Ref<string> = ref('')

/**
 * A password the administrator has just typed, held only until the save is sent.
 *
 * Starts empty on every load and after every save, and is never filled from the
 * panel's answer: there is no value in that answer to fill it from.
 */
const password: Ref<string> = ref('')

/** The address the panel's mail is sent from, as typed. */
const fromAddress: Ref<string> = ref('')

/** The display name beside the sender address, as typed. */
const fromName: Ref<string> = ref('')

/** Where alert mail goes, as typed. */
const alertRecipient: Ref<string> = ref('')

/** Where a test message should be sent, as typed. */
const testRecipient: Ref<string> = ref('')

/** True once the panel has answered, which is when the form has something to edit. */
const isLoaded: ComputedRef<boolean> = computed(() => {
  return smtpStore.settings !== null
})

/** True when the panel holds a submission password, which is all the screen may know about it. */
const hasStoredPassword: ComputedRef<boolean> = computed(() => {
  return smtpStore.settings?.hasPassword === true
})

/** The three protection modes, labelled in the interface language. */
const securityOptions: ComputedRef<readonly SelectOption[]> = computed(() => {
  return [
    { value: 'none', label: t('app.smtp.security.none') },
    { value: 'startTls', label: t('app.smtp.security.startTls') },
    { value: 'implicitTls', label: t('app.smtp.security.implicitTls') },
  ]
})

/**
 * Copies settings the panel reported into the form's fields.
 *
 * The password field is deliberately not among them, and is cleared instead: the
 * answer carries no password, and a field that kept the last typed one would resend
 * it on a save the administrator meant to leave the credential alone.
 * @param settings The settings to show, or `null` while none have been read.
 * @returns Nothing; the fields are updated synchronously.
 */
const fill = (settings: SmtpSettings | null): void => {
  if (settings === null) {
    return
  }

  host.value = settings.host
  // A panel that has never had settings reports port 0, which is not a port anybody
  // typed — the field stays empty rather than showing a number nobody chose.
  port.value = settings.port === 0 ? '' : String(settings.port)
  security.value = settings.security
  username.value = settings.username
  password.value = ''
  fromAddress.value = settings.fromAddress
  fromName.value = settings.fromName
  alertRecipient.value = settings.alertRecipient
}

/**
 * Saves the settings as typed.
 *
 * The password is omitted entirely when the field is empty, which is the
 * instruction that keeps the stored one; sending an empty string would clear it.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  await smtpStore.save({
    host: host.value,
    port: Number(port.value),
    // `security` is a plain string because `UiSelect`'s v-model is a string by contract; the
    // options above are exactly the enum's three values, so this narrows rather than guesses.
    security: security.value as SmtpSecurity,
    username: username.value,
    ...(password.value === '' ? {} : { password: password.value }),
    fromAddress: fromAddress.value,
    fromName: fromName.value,
    alertRecipient: alertRecipient.value,
  })
}

/**
 * Sends one test message to the stated address.
 *
 * Whatever the mail server refused with is rendered as the panel relayed it: that
 * sentence is what the administrator pressed the button for.
 * @returns Resolves once the request has settled.
 */
const sendTest = async (): Promise<void> => {
  await smtpStore.sendTest(testRecipient.value)
}

// The form follows whatever the panel last reported, including after a save.
watch(
  () => {
    return smtpStore.settings
  },
  fill,
  { immediate: true },
)

void smtpStore.load()
</script>

<template>
  <section class="w-full">
    <UiPageHeading
      class="mb-4"
      :eyebrow="t('app.smtp.eyebrow')"
      :title="t('app.smtp.heading')"
      :subtitle="t('app.smtp.subtitle')"
    />

    <!-- What needs attention comes first: a refusal (the mail server's own words) before
         the confirmations, which are good news and can wait a line. -->
    <div class="mb-4 flex flex-col gap-3 empty:hidden">
      <UiAlert v-if="smtpStore.errorMessage !== null" variant="error">
        {{ smtpStore.errorMessage }}
      </UiAlert>

      <UiAlert v-if="smtpStore.saved" variant="success">{{ t('app.smtp.saved') }}</UiAlert>

      <UiAlert v-if="smtpStore.testSent" variant="success">{{ t('app.smtp.testSent') }}</UiAlert>
    </div>

    <div v-if="isLoaded" class="grid items-start gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,24rem)]">
      <UiForm @submit="submit">
        <div class="flex flex-col gap-4">
          <UiCard>
            <UiSectionHeading class="mb-4" :title="t('app.smtp.connectionTitle')" />
            <div class="flex flex-col gap-3">
              <!-- Host and port are machine text: the mono face, never uppercased. -->
              <div class="grid gap-3 sm:grid-cols-[minmax(0,1fr)_8rem]">
                <UiInput v-model="host" :label="t('app.smtp.hostLabel')" mono required />
                <UiInput v-model="port" :label="t('app.smtp.portLabel')" mono required />
              </div>

              <UiSelect
                v-model="security"
                :label="t('app.smtp.securityLabel')"
                :options="securityOptions"
                required
              />
            </div>
          </UiCard>

          <UiCard>
            <UiSectionHeading class="mb-4" :title="t('app.smtp.credentialsTitle')" />
            <div class="flex flex-col gap-3">
              <UiInput v-model="username" :label="t('app.smtp.usernameLabel')" />

              <!-- No `generate`: an existing provider secret is being entered here, not
                   minted. The hint below stands in for the stored value, which nothing
                   on this screen — or in the response behind it — can render. -->
              <UiPasswordInput
                v-model="password"
                :label="t('app.smtp.passwordLabel')"
                :placeholder="t('app.smtp.passwordPlaceholder')"
                autocomplete="new-password"
              />
              <p class="flex items-start gap-2 text-sm text-text-secondary">
                <UiIcon class="mt-0.5 shrink-0" name="lock" size="sm" />
                <span>{{ hasStoredPassword ? t('app.smtp.hasPassword') : t('app.smtp.noPassword') }}</span>
              </p>
            </div>
          </UiCard>

          <UiCard>
            <UiSectionHeading class="mb-4" :title="t('app.smtp.senderTitle')" />
            <div class="flex flex-col gap-3">
              <UiInput v-model="fromAddress" :label="t('app.smtp.fromAddressLabel')" required />
              <UiInput v-model="fromName" :label="t('app.smtp.fromNameLabel')" />
              <UiInput v-model="alertRecipient" :label="t('app.smtp.alertRecipientLabel')" required />
            </div>
          </UiCard>

          <div>
            <UiButton type="submit" :disabled="smtpStore.loading">
              {{ t('app.smtp.save') }}
            </UiButton>
          </div>
        </div>
      </UiForm>

      <UiCard>
        <UiSectionHeading
          class="mb-4"
          :title="t('app.smtp.testTitle')"
          :subtitle="t('app.smtp.testDescription')"
        />

        <UiForm @submit="sendTest">
          <div class="flex flex-col gap-3">
            <UiInput
              v-model="testRecipient"
              :label="t('app.smtp.testRecipientLabel')"
              :placeholder="t('app.auth.emailPlaceholder')"
              required
            />
            <div>
              <UiButton type="submit" variant="secondary" :disabled="smtpStore.loading">
                {{ t('app.smtp.sendTest') }}
              </UiButton>
            </div>
          </div>
        </UiForm>
      </UiCard>
    </div>
  </section>
</template>
