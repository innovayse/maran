/**
 * The merged file-transfer login domain: the one row shape the "File transfer" screen renders, and
 * the protocol union that says which daemon a row belongs to.
 *
 * It exists because the composition is the SPA's own, not the server's. SFTP logins and FTPS logins
 * are separate modules with separate endpoints on the panel (`/api/v1/sftp-users`,
 * `/api/v1/ftp-users`), and they stay separate there. One account holds logins of both kinds, and an
 * operator asking "who can write into this account's files" is asking one question — so the two
 * lists are merged HERE, in the layer whose job is composition, and no merged endpoint is asked for.
 */

/**
 * Which transfer daemon a login authenticates against, as this bundle is able to say it.
 *
 * **`unknown` is a member on purpose and it is the important one.** The two protocols are different
 * system users on one host, and a row labelled with the wrong one tells a customer to point the
 * wrong client at it. A protocol token a newer panel knows and this bundle does not therefore
 * becomes `unknown` and renders as absence, rather than falling back to either real value: guessing
 * would put an FTPS login under an SFTP label, which is the direction that produces a confident,
 * wrong instruction. Absence of a token is a different question and is answered separately — see
 * `utils/fileTransferProtocolOf.ts`.
 */
export type FileTransferProtocol = 'sftp' | 'ftps' | 'unknown'

/**
 * One login on the merged screen, whichever daemon it belongs to.
 *
 * Deliberately narrower than either source DTO: it holds what the merged table renders and nothing
 * else. A screen that carried both source shapes would have to branch per column, and the branch
 * would be the place the two drift apart.
 */
export interface FileTransferLogin {
  /** The login's identity within its own module — unique per protocol, not across both. */
  id: string
  /** The account that owns this login. */
  accountId: string
  /**
   * The system login the host holds — `<account>_<name>`, as it appears in `/etc/passwd`.
   *
   * This is what the customer types into their client. It is the server's own spelling and is never
   * assembled here from an account name and a suffix.
   */
  fullName: string
  /**
   * Which daemon accepts this login.
   *
   * The whole point of the merged screen: the two protocols are different system users on one host,
   * and a row that did not say which one it is would be telling the operator that either client
   * would work.
   */
  protocol: FileTransferProtocol
  /** The instant the login was created, as an ISO-8601 string. */
  createdAt: string
}

/**
 * What the merged create form emits: the same two fields both modules take, plus the choice of which
 * module is asked.
 *
 * The protocol is a routing decision made in the SPA and never sent anywhere — the page dispatches
 * to `/api/v1/sftp-users` or `/api/v1/ftp-users` accordingly, and neither request body carries it.
 * That is why this shape lives here and not beside either module's own request type: it belongs to
 * the composition, which is the SPA's.
 */
export interface CreateFileTransferLoginRequest {
  /** The account that will own the login. */
  accountId: string
  /** The login name, without the account prefix; lowercase letters and digits only. */
  name: string
  /** Which daemon the login is created for. Never `unknown` — the form offers two real choices. */
  protocol: 'sftp' | 'ftps'
}
