/**
 * Which kind of storage a destination names, mirroring the backend's `BackupDestinationKind`.
 *
 * The values are lowercase because the panel registers
 * `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` for every enum it sends and binds, so
 * `BackupDestinationKind.S3` travels as `"s3"`. A capitalised union here would also fail the
 * `maran api` gate, which lowercases the first letter of each C# member before comparing.
 *
 * **Both members exist so a remote destination can be DESCRIBED, and only one of them can be acted
 * on.** The panel's `RemoteDestinationPolicy` admits `local` alone; every path that would act on
 * `s3` refuses. The union is not narrowed to one member for the backend's own reason: a build with
 * no word for the remote kind can only report it as an unknown value, which reads as a typo rather
 * than as the honest state of the product.
 */
export type BackupDestinationKind = 'local' | 's3'

/**
 * Outward view of one backup destination, mirroring the backend's `BackupDestinationDto` field for
 * field — six members, and there is no seventh.
 *
 * **It carries no credential, and deliberately declares none.** No destination on this build holds
 * a secret: the remote arm brings its columns on the day the owner says yes, and until then there
 * is nothing stored that a response could leak. Because this interface names every field the screen
 * reads, a key the server one day added to the payload would be dropped on the floor rather than
 * rendered — which is the property that matters for a shape that will eventually grow keys.
 */
export interface BackupDestination {
  /** The destination's identity. A schedule may name it; the screen shows it as machine text. */
  id: string
  /**
   * The label as the row stores it: machine-stable, never translated, and what an operator sees in
   * `psql` and names in a support ticket. For a destination an operator saved, it is the words they
   * typed.
   */
  name: string
  /**
   * The same label as a screen shows it, localized by the backend.
   *
   * It differs from {@link BackupDestination.name} for exactly one destination — the one this panel
   * seeded and named itself, which the backend renders in the caller's language while the row keeps
   * its English name — and equals it for every destination an operator named. A screen therefore
   * shows this and, only when the two differ, the stored name beside it: an operator-named row must
   * never be silently relabelled, and a stored value must never become unreachable.
   */
  displayName: string
  /** Which kind of storage it names. */
  kind: BackupDestinationKind
  /**
   * Where a local destination's artifacts rest, as the AGENT stated it on this request, and `null`
   * when the panel could not establish it — the agent was unreachable, or is older than the field.
   *
   * The panel holds no value of its own: the agent writes under its own constant and refuses to be
   * told another, which is why no surface accepts a path — and why the screen must say the path is
   * not established rather than show the directory it would have guessed.
   */
  path: string | null
  /** Whether backups naming no destination are written here. Exactly one destination is the default. */
  isDefault: boolean
  /** When the panel first recorded it, as an ISO-8601 string. */
  createdAt: string
}

/**
 * Typed access to the backup-destinations endpoints.
 *
 * One call, and not two. `POST /api/v1/backup-destinations` exists and is not reached from this
 * panel: its handler has no success arm, so every request it can be given is answered with a
 * refusal, and a screen control whose only outcome is a refusal is a promise the product cannot
 * keep. The endpoint stays valuable to an API caller — it answers with a machine-stable code and a
 * localized sentence instead of a 404 on a route nobody wrote — and it gains a `save` here on the
 * day the panel can store a destination.
 *
 * There is no delete either, because the backend publishes none: the one destination that exists is
 * the default, which every historic backup row's null already points at.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface BackupDestinationsApi {
  /**
   * Reads every destination this server records, the default one first.
   *
   * Administrators only: a signed-in customer is answered **403**, never 404, because a destination
   * carries no account and there is no tenant fact a 404 could conceal.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The destinations as the panel holds them.
   */
  list: (signal?: AbortSignal) => Promise<BackupDestination[]>
}
