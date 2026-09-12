#!/usr/bin/env bash
# A LIBRARY, not a command: `scripts/lib/run-dev.sh` sources this file.
#
# THE TEARDOWN DECISION, AND NOTHING THAT ACTS ON IT. This file holds one pure function, and it is
# a file of its own because that separation is the whole point: the decision is DATA that can be
# read before any container is touched, while the execution lives next door in
# `scripts/lib/dev-teardown.sh`. Both teardown defects this command has carried were about WHICH
# containers the teardown named, and an assertion on the outcome cannot see them.
#
# Keeping the two apart is what makes the self-check able to fail: it asks the `dev` instance for
# its plan — the instance whose compose project is the same one the database lives in, which is
# where both defects were — without standing that instance up, and asserts the argument lists
# themselves. A check that only watched the self-check tear ITSELF down would report on a branch
# the defect was never in.

# dev_teardown_plan: the `docker compose` argument lists a run's teardown will issue, one per
# line, in the order it will issue them — as DATA, before any of it happens.
#
# `dev_teardown_stop_stack` executes precisely this list, so it is the decision itself rather than
# a description of one. Everything it needs is an argument: the instance, its compose project, and
# whether the database was already running when the run began (1 found, 0 started by this run).
dev_teardown_plan() {
  local plan_instance="$1" plan_project="$2" plan_postgres_was_running="$3"
  if [ "$plan_instance" = "selfcheck" ]; then
    # `down` is safe for THIS instance and only for this one: `maran-selfcheck` contains the agent
    # service alone, because postgres was started under the default project `maran`.
    echo "-p $plan_project --profile agent down --remove-orphans"
  else
    # NAME THE SERVICE. A bare `stop` here is `docker compose -p maran --profile agent stop`, and
    # the `dev` instance's project IS the default project the database lives in — so it stopped
    # `maran-postgres` too. Measured by this command's first outside user: a shared database
    # container that had been `Up 23 hours (healthy)` was `Exited (0)` six seconds after somebody
    # else's Ctrl+C, and a peer's panel, `maran drift` or integration suite would have lost its
    # server without a word.
    echo "-p $plan_project --profile agent stop agent"
  fi
  # The database, only when this run started it — in BOTH instances. This used to live inside the
  # self-check branch, which is the one instance that needed it least and the only one that had it.
  if [ "$plan_postgres_was_running" -eq 0 ]; then
    echo "stop postgres"
  fi
}
