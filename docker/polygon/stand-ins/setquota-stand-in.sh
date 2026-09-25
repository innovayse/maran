#!/bin/sh
# A stand-in for setquota, installed at /usr/sbin/setquota in the polygon
# images ONLY. Production never uses Docker (spec §2) and never sees this file.
#
# The path matters and used to be wrong. It sat in /usr/local/bin, which worked
# only because the agent spawned `setquota` by its bare name and PATH found the
# stand-in ahead of the real tool — the very substitution a root daemon must not
# be open to, demonstrated by this repository's own test images. The agent now
# names the absolute path its distro adapter gives, so the stand-in has to be AT
# that path to stand in for anything.
#
# Why it exists: creating an account applies its disk quota, and a container's
# overlay filesystem has no quota support to apply one to — `setquota` refuses
# on any host where the filesystem was not mounted with quotas enabled. Without
# a stand-in, every polygon test would fail at account creation and none of the
# privilege or nginx behaviour they exist for would ever be reached.
#
# It accepts and does nothing. The consequence is stated rather than hidden:
# quota behaviour is NOT exercised by the polygon, and AccountError::CommandFailed
# from a refusing setquota stays covered by the ops::accounts unit tests, which
# assert the argv rather than the effect.
exit 0
