-- Unit tests of the DKIM signing condition, run with a plain Lua interpreter from
-- agent/crates/templates: lua tests/lua/maran_dkim_condition_test.lua
-- No Rspamd globals exist here, so the file returns its pure part.
local M = dofile('templates/rspamd/lua/maran_dkim_condition.lua')

local failures = 0
local function check(name, condition)
  if not condition then
    failures = failures + 1
    print('FAIL: ' .. name)
  end
end

-- the authenticated submission road is gated like the local one: a login says nothing
-- about the domain the From: header names (issue #531 review)
check('authenticated submission of a bound domain is signed', M.decide('ORIGINATING', { 'a.test' }, 'a.test') == true)
check('authenticated submission without proof is not signed', M.decide('ORIGINATING', { 'a.test' }, nil) == false)
check('authenticated submission of another tenant domain is not signed', M.decide('ORIGINATING', { 'victim.test' }, 'a.test') == false)
check('one foreign From among bound ones on the login road is not signed', M.decide('ORIGINATING', { 'a.test', 'victim.test' }, 'a.test') == false)
check('a subdomain of the bound domain is not the bound domain', M.decide('ORIGINATING', { 'shop.a.test' }, 'a.test') == false)
check('the bound domain is not a subdomain of the signed one', M.decide('ORIGINATING', { 'a.test' }, 'shop.a.test') == false)

-- the local road: proof required, and it must name the From domain
check('a bound local message is signed', M.decide('MARAN_LOCAL', { 'a.test' }, 'a.test') == true)
check('the proof is compared case-insensitively', M.decide('MARAN_LOCAL', { 'A.Test' }, 'a.test') == true)
check('a local message without proof is not signed', M.decide('MARAN_LOCAL', { 'a.test' }, nil) == false)
check('an empty proof is not a proof', M.decide('MARAN_LOCAL', { 'a.test' }, '') == false)
check('a proof for another domain does not sign this From', M.decide('MARAN_LOCAL', { 'victim.test' }, 'a.test') == false)
check('one foreign From among bound ones is not signed', M.decide('MARAN_LOCAL', { 'a.test', 'victim.test' }, 'a.test') == false)
check('a local message with no From domain is not signed', M.decide('MARAN_LOCAL', {}, 'a.test') == false)

-- an internationalised From: is compared in its A-label form only
check('an A-label From matching the proof is signed', M.decide('MARAN_LOCAL', { 'xn--bcher-kva.example' }, 'xn--bcher-kva.example') == true)
check('a Unicode From is not signed even when the proof is its A-label', M.decide('MARAN_LOCAL', { 'b\195\188cher.example' }, 'xn--bcher-kva.example') == false)
check('a Unicode From is not signed on the login road either', M.decide('ORIGINATING', { 'b\195\188cher.example' }, 'xn--bcher-kva.example') == false)

-- everything else: never. This is the port 25 hole: a customer connects to 127.0.0.1
-- and types any From; the road is INBOUND and nothing can bind it.
check('inbound mail is never signed', M.decide('INBOUND', { 'a.test' }, nil) == false)
check('inbound mail is not signed even with a proof', M.decide('INBOUND', { 'a.test' }, 'a.test') == false)
check('a missing daemon name is not signed', M.decide(nil, { 'a.test' }, 'a.test') == false)
check('an unknown daemon name is not signed', M.decide('other', { 'a.test' }, 'a.test') == false)

-- what the module is handed: a full table, or false (an empty table signs nothing)
local params = M.parameters('A.Test', 'sel1', '/k/$domain.$selector.key')
check('a domain with a selector yields domain, selector and key', params.domain == 'a.test' and params.selector == 'sel1' and params.key == '/k/$domain.$selector.key')
check('a domain with no selector is not signed', M.parameters('a.test', nil, '/k/$domain.$selector.key') == false)
check('an empty selector is not signed', M.parameters('a.test', '', '/k/$domain.$selector.key') == false)
check('no key template is not signed', M.parameters('a.test', 'sel1', nil) == false)

if failures > 0 then
  os.exit(1)
end
print('ok')
