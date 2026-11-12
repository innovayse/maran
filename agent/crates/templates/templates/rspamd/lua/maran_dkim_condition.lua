-- Maran: decides WHICH mail the DKIM signing module signs (issue #531). Installed by
-- the Maran agent and named by `sign_condition` in local.d/dkim_signing.conf; hand
-- edits are overwritten on the next apply.
--
-- Signing puts a domain's reputation on a message, so it must follow who sent the
-- message and never where the connection came from or what the From: header says:
-- a customer process reaches 127.0.0.1 as easily as the mail server does, and a
-- mailbox login says nothing about the domain a message names in its From: header.
--
--   ORIGINATING   the authenticated submission service
--   MARAN_LOCAL   mail a system user handed to sendmail
--
-- Both roads are signed ONLY when the local-submission rule (maran_local_submit.lua)
-- recorded on this very message that its From: domain is bound to the submitting
-- account (the login's account, or the system user's), and the domain still equals
-- the one signed for. A message of another tenant's domain, or one the rule refused,
-- carries no proof.
--
-- Root and the Postfix owner are not customers: the rule gives their mail the proof of
-- its From: domain only when that domain is the host's own mail name (a setting the
-- agent renders), and the signing module then signs it when the host holds a key for
-- that name; with no key nothing is signed. A trusted uid never obtains the proof of a
-- tenant's domain. A From: domain in its Unicode spelling is never signed here: the rule
-- binds the A-label (punycode) spelling and the key files are named by it, so only a
-- header that already carries the A-label can be proven equal.
--   anything else (INBOUND, an unknown or missing daemon name): never signed.
--
-- The decision is a pure function (M.decide) so tests can drive it with plain
-- values; only the closure at the bottom touches the Rspamd API.
--
-- HOW RSPAMD USES THIS FILE (verified on Rspamd 3.8 to 4.2): `sign_condition` in
-- dkim_signing.conf is Lua SOURCE evaluated per message, not a path, so it is
-- `return dofile("<this file>")(...)` and this file returns the function. With a
-- condition the module signs with ONLY what the function returns: a table
-- { domain, selector, key } to sign, or false/nil to leave the message unsigned
-- (an empty table is an error and signs nothing). The module's own selector map and
-- key path are therefore read here, from its own settings, and handed back.

local M = {}

-- The daemon names master.cf gives each road (milter_macro_daemon_name).
M.ORIGINATING = 'ORIGINATING'
M.LOCAL = 'MARAN_LOCAL'

-- True when a message of this road with these From: domains and this recorded proof
-- is to be signed. `proven` is the domain the local-submission rule bound, or nil.
-- The same rule serves both roads: EVERY From: domain must equal the proof (the
-- module's own multiple-From checks are skipped under a condition), and a domain in
-- its Unicode spelling is never signed (see the header).
function M.decide(daemon_name, from_domains, proven)
  if daemon_name ~= M.LOCAL and daemon_name ~= M.ORIGINATING then
    return false
  end
  if type(proven) ~= 'string' or proven == '' then
    return false
  end
  if type(from_domains) ~= 'table' or #from_domains == 0 then
    return false
  end
  for _, domain in ipairs(from_domains) do
    if tostring(domain):find('[\128-\255]') or tostring(domain):lower() ~= proven:lower() then
      return false
    end
  end
  return true
end

-- What the signing module needs for one domain, or false when the domain has no
-- selector (nothing to sign with). `selector` is the selector map's answer for the
-- domain, `key` the module's key path template (it expands $domain and $selector).
function M.parameters(domain, selector, key)
  if type(selector) ~= 'string' or selector == '' or type(key) ~= 'string' or key == '' then
    return false
  end
  return { domain = tostring(domain):lower(), selector = selector, key = key }
end

-- ---------------------------------------------------------------------------
-- Rspamd binding. Verified on a live Rspamd 3.8 to 4.2 (container): the chunk is
-- run through sign_condition as above, task:cache_get returns the proof the
-- local-submission prefilter left, task:get_request_header('MTA-Name') carries the
-- road. The selector map is read once per process and kept in package.loaded,
-- because the file is evaluated again for every message.
-- ---------------------------------------------------------------------------
if rspamd_config == nil then
  return M
end

local opts = rspamd_config:get_all_opt('maran_local_submit')
-- The name of the proof, the one spelling the agent renders into the rule's settings.
-- Without it nothing can be proven, so nothing is signed.
local proof_key = opts and opts.bound_domain_key
local dkim = rspamd_config:get_all_opt('dkim_signing')

local CACHE = 'maran_dkim_selectors'
local selectors = package.loaded[CACHE]
if selectors == nil and dkim and dkim.selector_map then
  local lua_maps = require 'lua_maps'
  selectors = lua_maps.map_add_from_ucl(dkim.selector_map, 'map', 'Maran DKIM selectors')
  package.loaded[CACHE] = selectors
end

return function(task)
  if not proof_key or not selectors or not dkim or not dkim.path then
    return false
  end
  local from_domains = {}
  for _, address in ipairs(task:get_from('mime') or {}) do
    -- An unreadable domain is kept as the empty name, which equals no proof.
    from_domains[#from_domains + 1] = (address.domain and address.domain or '')
  end
  local daemon_name = task:get_request_header('MTA-Name')
  local proven = task:cache_get(proof_key)
  if not M.decide(daemon_name and tostring(daemon_name) or nil, from_domains, proven) then
    return false
  end
  return M.parameters(proven, selectors:get_key(proven:lower()), dkim.path)
end
