# Customer self-service matrix (issue #147)

Every operation a customer's token can reach, with the plan feature that gates it, how it behaves while
an operator is viewing the panel as that customer, and the integration test class that proves another
account's resource is answered 404 (never 403). The machine-checked source is
`backend/tests/Maran.ArchitectureTests/Baselines/customer-operations.txt`; `CustomerOperationMatrixTests`
fails when that file and the routes the panel serves disagree, and when a named class is declared by no
integration test file. This page restates the rows added with the isolation tests of issue #147.

| Operation | Endpoint | Plan feature | View as | Isolation test class |
|---|---|---|---|---|
| Choices for narrowing a restore | `GET api/v1/backups/{id}/restore-choices` | Backups | Read | `BackupsAuthorizationTests` |
| Read a site's options | `GET api/v1/sites/{id}/options` | Sites | Read | `SitesAuthorizationTests` |
| Replace a site's options | `PUT api/v1/sites/{id}/options` | Sites | Refused | `SitesAuthorizationTests` |
| Read the caller's dashboard layout | `GET api/v1/me/dashboard-layout` | none | Read | none: reads only the caller's own saved layout |
| Save the caller's dashboard layout | `PUT api/v1/me/dashboard-layout` | none | Refused | none: changes only the caller's own saved layout |
| Read the account's PHP settings | `GET api/v1/php-settings` | PHP | Read | `PhpSettingsTenantIsolationEndpointTests` |
| Change the account's PHP settings | `PUT api/v1/php-settings` | PHP | Refused | `PhpSettingsTenantIsolationEndpointTests` |
| Read which certificate kinds a domain can have | `GET api/v1/certificates/issuance-options` | SSL | Read | `CertificatesAuthorizationTests` |

Notes:

- **Restore choices** lists the account's current databases and sites, not what the archive holds. Another
  customer's backup answers 404, and so does an invented identifier.
- **Site options** are scoped by the Sites tenant filter. The replace is idempotent and, for an enabled
  site, goes through the agent, so with the agent down the owner is answered 503 while a stranger's site
  is still 404 (the filter answers before the agent is asked).
