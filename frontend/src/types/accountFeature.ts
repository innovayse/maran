/**
 * The name of one single feature a hosting plan can sell, exactly as it travels on the wire: the
 * backend's `AccountFeature` members, camel-cased (`fileTransfer`, not `FileTransfer`). The panel
 * sends and accepts a feature only by one of these names — never a number, never a combination, and
 * never `none` or `all`, which name sets and not features.
 *
 * This is the contract's vocabulary, the type of a `features` list in a request or a response. The
 * SPA still holds no list of the features a plan may sell: the plan form draws the choices from
 * `GET /api/v1/plans/features`, so a feature added to the backend appears there without a release
 * (and is added to this union in the same change, which the type check then follows through the
 * screens that name one).
 */
export type AccountFeatureName = 'mail' | 'dns' | 'databases' | 'cron' | 'fileTransfer' | 'backups'
