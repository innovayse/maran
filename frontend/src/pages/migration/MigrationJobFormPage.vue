<script setup lang="ts">
/**
 * New migration job screen (issue #197, phase 2): chooses the registered server to move, names the job, sets how
 * many accounts are transferred at once and how fast, and creates a draft holding every account the server's last
 * discovery found. Mapping each account to a plan and an owner, the dry run and the start are the job's own screen.
 *
 * **A server whose discovery has not run offers nothing to move**, and the form says so instead of creating an empty
 * job. A server that is a Maran panel also needs the origin of its pull endpoint and the key fingerprint the
 * administrator confirmed against it, because it is moved by package and not by copy.
 *
 * Validation here is advice; the backend refuses a value that is not valid.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSelect, { type SelectOption } from '../../components/ui/UiSelect.vue'
import { useMigrationJobsStore } from '../../stores/migrationJobs'
import { useRemoteServersStore } from '../../stores/remoteServers'
import { flashNoticeState } from '../../utils/flashNotice'
import type { MigrationJobRequest } from '../../types/migrationJob'
import type { RemoteServer } from '../../types/migrationSource'

/** How many servers the picker offers; a panel registers a handful. */
const SERVER_CHOICES = 100

/** The page size discovered accounts are read with when collecting every id. */
const ACCOUNT_PAGE_SIZE = 100

const { t } = useI18n()
const router = useRouter()
const jobs = useMigrationJobsStore()
const servers = useRemoteServersStore()

/** The chosen server. */
const serverId: Ref<string> = ref('')

/** The label. */
const name: Ref<string> = ref('')

/** How many accounts at once, as typed. */
const concurrency: Ref<string> = ref('2')

/** The bandwidth limit in KiB/s, as typed; empty for none. */
const bandwidth: Ref<string> = ref('')

/** The pull origin of a Maran source. */
const sourceUrl: Ref<string> = ref('')

/** The confirmed key fingerprint of a Maran source. */
const fingerprint: Ref<string> = ref('')

/** Whether a submit was attempted, so errors show only after. */
const submitted: Ref<boolean> = ref(false)

/** Whether the accounts are being collected and the job created. */
const working: Ref<boolean> = ref(false)

/** The panel's message for a refused collection or creation, or `null`. */
const refusal: Ref<string | null> = ref(null)

/** The servers the picker offers. */
const choices: Ref<RemoteServer[]> = ref([])

/** The picker's options. */
const options: ComputedRef<SelectOption[]> = computed(() => {
  return choices.value.map((server) => {
    return { value: server.id, label: `${server.name} (${server.host})`, disabled: (server.accountsFound ?? 0) === 0 }
  })
})

/** The chosen server, or `undefined`. */
const chosen: ComputedRef<RemoteServer | undefined> = computed(() => {
  return choices.value.find((server) => {
    return server.id === serverId.value
  })
})

/** Whether the chosen server is a Maran panel. */
const isMaran: ComputedRef<boolean> = computed(() => {
  return chosen.value?.kind === 'maran'
})

/** The label's error. */
const nameError: ComputedRef<string | null> = computed(() => {
  return submitted.value && name.value.trim() === '' ? t('migrationJobs.form.errors.name') : null
})

/** The server's error. */
const serverError: ComputedRef<string | null> = computed(() => {
  return submitted.value && serverId.value === '' ? t('migrationJobs.form.errors.server') : null
})

/** The concurrency's error. */
const concurrencyError: ComputedRef<string | null> = computed(() => {
  const value = Number(concurrency.value)
  return submitted.value && !(Number.isInteger(value) && value >= 1 && value <= 8) ? t('migrationJobs.form.errors.concurrency') : null
})

/** The bandwidth's error. */
const bandwidthError: ComputedRef<string | null> = computed(() => {
  const text = bandwidth.value.trim()
  return submitted.value && text !== '' && !/^\d+$/.test(text) ? t('migrationJobs.form.errors.bandwidth') : null
})

/** The Maran source fields' error. */
const maranError: ComputedRef<string | null> = computed(() => {
  const missing = sourceUrl.value.trim() === '' || !/^[0-9a-f]{64}$/.test(fingerprint.value.trim())
  return submitted.value && isMaran.value && missing ? t('migrationJobs.form.errors.maran') : null
})

/**
 * Collects the identity of every account the server's last discovery found, page by page.
 * @param id The server.
 * @returns The ids, or `null` when a page was refused (the message is in {@link refusal}).
 */
const collectAccountIds = async (id: string): Promise<string[] | null> => {
  const ids: string[] = []
  let page = 1
  while (true) {
    const answer = await servers.listAccounts(id, { page, pageSize: ACCOUNT_PAGE_SIZE, search: '', sort: '', filters: {} })
    if (answer.kind !== 'loaded') {
      refusal.value = answer.kind === 'refused' ? answer.message : t('common.list.unreachable')
      return null
    }
    ids.push(...answer.page.items.map((account) => {
      return account.id
    }))
    if (ids.length >= answer.page.total || answer.page.items.length === 0) {
      return ids
    }
    page += 1
  }
}

/**
 * Creates the draft and opens it.
 * @returns Resolves once the request settled.
 */
const submit = async (): Promise<void> => {
  submitted.value = true
  refusal.value = null
  if (nameError.value !== null || serverError.value !== null || concurrencyError.value !== null || bandwidthError.value !== null || maranError.value !== null) {
    return
  }
  working.value = true
  try {
    const ids = await collectAccountIds(serverId.value)
    if (ids === null) {
      return
    }
    const request: MigrationJobRequest = {
      serverId: serverId.value,
      name: name.value.trim(),
      discoveredAccountIds: ids,
      concurrency: Number(concurrency.value),
    }
    if (bandwidth.value.trim() !== '') {
      request.bandwidthLimitKib = Number(bandwidth.value.trim())
    }
    if (isMaran.value) {
      request.sourceUrl = sourceUrl.value.trim()
      request.expectedKeyFingerprint = fingerprint.value.trim()
    }
    const created = await jobs.create(request)
    if (created === null) {
      refusal.value = jobs.actionErrorMessage ?? t('common.list.unreachable')
      return
    }
    await router.push({
      name: 'migration-job-detail',
      params: { id: created.id },
      state: flashNoticeState(t('migrationJobs.form.created', { name: created.name })),
    })
  } finally {
    working.value = false
  }
}

onMounted(async () => {
  jobs.clearErrors()
  const answer = await servers.list({ page: 1, pageSize: SERVER_CHOICES, search: '', sort: 'name', filters: {} })
  if (answer.kind === 'loaded') {
    choices.value = answer.page.items
  }
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('migrationJobs.form.heading')" :subtitle="t('migrationJobs.form.subtitle')" />

    <UiCard>
      <UiForm @submit="submit">
        <div class="flex min-w-0 max-w-xl flex-col gap-4">
          <UiAlert v-if="refusal !== null" variant="error">{{ refusal }}</UiAlert>
          <UiSelect
            v-model="serverId"
            :label="t('migrationJobs.form.server')"
            :options="options"
            :placeholder="t('migrationJobs.form.serverPlaceholder')"
            :error="serverError"
            required
          />
          <UiInput v-model="name" :label="t('migrationJobs.form.name')" :error="nameError" required />
          <UiInput v-model="concurrency" :label="t('migrationJobs.form.concurrency')" :error="concurrencyError" required />
          <UiInput v-model="bandwidth" :label="t('migrationJobs.form.bandwidth')" :error="bandwidthError" />
          <template v-if="isMaran">
            <UiAlert variant="info">{{ t('migrationJobs.form.maranNote') }}</UiAlert>
            <UiInput v-model="sourceUrl" :label="t('migrationJobs.form.sourceUrl')" :error="maranError" required />
            <UiInput v-model="fingerprint" :label="t('migrationJobs.form.fingerprint')" required />
          </template>
          <div class="flex items-center gap-2">
            <UiButton type="submit" :disabled="working">{{ t('migrationJobs.form.create') }}</UiButton>
            <UiButton variant="secondary" :to="{ name: 'migration-jobs' }">{{ t('common.cancel') }}</UiButton>
          </div>
        </div>
      </UiForm>
    </UiCard>
  </section>
</template>
