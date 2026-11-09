# Per-site request limit

How a site's request limit works, what it limits, and what it does not (issue #159). The limit is one
of the site's options (issue #151): it is set on the site's Options tab, stored with the other
options and saved with them as one set.

## What it limits

A limit is two whole numbers, or nothing:

- **Requests per second**, 1 to 1000. How many requests one visitor address may make to the site each
  second, on average.
- **Extra requests allowed at once (burst)**, 0 to 1000. How many requests above that rate one address
  may send in a short spell before the site refuses. A page that loads many files together needs a
  burst; with a burst of 0 every request above the rate is refused at once. A burst only has meaning
  with a rate: the panel's screen refuses a burst on its own, and the agent refuses it too.

A site with no limit has no value stored at all. There is no "limit of zero": an empty rate means
unlimited.

A request over the limit is answered with HTTP status **429** (too many requests) and is not passed
on to the site. The bounds are stated by the panel in the options answer (`limits`), so the screen
shows the server's own numbers.

## Who may set it

The site's owner, from the Options tab, and an administrator on any site. A customer who limits their
own site only slows their own visitors, so there is no plan limit and no separate permission: the
options endpoints already require the Sites feature and answer 404 for another account's site. The
change is refused while an operator is viewing the panel as a customer (view-as), like every other
change to a site's options.

## What the limit is keyed on

The limit counts the **address that connects to the web server**. Behind a reverse proxy or a CDN
that is the proxy's address, so every visitor behind it shares one limit, and a limit meant for one
abusive visitor throttles them all. If a site sits behind a proxy, leave the limit empty or set it
high enough for the whole proxy's traffic.

## How it reaches the web server

The panel stores the limit in the site's options row (`RequestRatePerSecond` and `RequestRateBurst`,
both null or both set, enforced by a check constraint) and sends it to the agent with the rest of the
options (`SiteOptions.request_rate_per_second` and `request_burst`). The agent re-validates both
numbers against the same bounds and renders them into the site's virtual host. Every re-render of a
vhost carries the stored limit, because the options travel with the site: enabling or disabling the
site, changing its PHP version, the start-up reconcile run and installing or removing a certificate
all build their description from the stored row. A suspended site serves a stub with no limit; the
limit is kept and takes effect when the site is enabled again.

## What the journal records

Saving options writes one audit entry for the site. The entry names the groups that changed (the
request limit is the group `request-rate`) and never the values.

## Not covered

- A limit per URL path, per user or per country. The limit is per visitor address, per site.
- Geo blocking, which needs a country-to-address dataset and is a separate issue.
