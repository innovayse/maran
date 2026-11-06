<script setup lang="ts">
/**
 * The ACME directory the panel orders certificates from: its kind and host, read only. Drawn for
 * administrators, who are the only role the panel tells; it draws nothing when it was not given a
 * directory, so a failed read leaves no hole and no error.
 */
import { useI18n } from "vue-i18n";
import UiBadge from "../ui/UiBadge.vue";
import type { AcmeDirectory } from "../../types/certificate";

/** Props accepted by {@link AcmeDirectoryLine}. */
const props = defineProps<{
  /** The directory the panel reported, or `null` when it was not read. */
  directory: AcmeDirectory | null;
}>();

const { t } = useI18n();
</script>

<template>
  <p v-if="props.directory !== null" class="flex flex-wrap items-center gap-2 text-sm text-text-muted">
    <span>{{ t("certificates.acmeDirectory.label") }}</span>
    <!-- The kind's name is the panel's own sentence; only a staging directory, whose certificates no
         browser trusts, is flagged. Production and a private authority are plain facts, not statuses. -->
    <UiBadge :variant="props.directory.kind === 'staging' ? 'warning' : 'neutral'">
      {{ props.directory.kindName }}
    </UiBadge>
    <span class="font-mono text-text-primary wrap-anywhere">{{ props.directory.host }}</span>
  </p>
</template>
