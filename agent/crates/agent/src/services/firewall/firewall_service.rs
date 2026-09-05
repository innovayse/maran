//! `FirewallService`: the host's nftables policy and the addresses it drops.

use std::sync::Arc;

use maran_distro::DistroAdapter;
use maran_ops::firewall::{self, FirewallHost};
use tonic::{Request, Response, Status};

use crate::proto::firewall_service_server::FirewallService;
use crate::proto::{
    AllowPortOk, AllowPortRequest, AllowPortResponse, BanAddressOk, BanAddressRequest,
    BanAddressResponse, DenyPortOk, DenyPortRequest, DenyPortResponse, ListBansOk, ListBansRequest,
    ListBansResponse, ListRulesOk, ListRulesRequest, ListRulesResponse, UnbanAddressOk,
    UnbanAddressRequest, UnbanAddressResponse, allow_port_response, ban_address_response,
    deny_port_response, list_bans_response, list_rules_response, unban_address_response,
};
use crate::services::firewall::firewall_status::to_agent_error;
use crate::services::firewall::listed_ban::listed_ban;
use crate::services::firewall::listed_rule::listed_rule;
use crate::services::firewall::validated_address::validated_address;
use crate::services::firewall::validated_ban::validated_ban;
use crate::services::firewall::validated_ports::validated_ports;
use crate::services::firewall::validated_rule::validated_rule;
use crate::services::wire::run_blocking::run_blocking;

/// Serves the host firewall operations over the wire.
///
/// Every rpc follows the same three steps: rebuild the request into validated
/// types, run one operation, and map the outcome into the response's `oneof`.
/// Failures travel in the payload rather than as a gRPC status, because they
/// are answers the panel acts on — a rule that is already there is information,
/// not a transport error (rules/proto.md).
///
/// # Every operation goes through the blocking pool, including the two that
/// would not complain
///
/// `ops::firewall` is synchronous, and its module documentation makes calling
/// it from `spawn_blocking` a REQUIREMENT on this file rather than a
/// description of what it happens to do. Five of the operations take the area's
/// process-wide lock with `blocking_lock`, which PANICS when it is called from
/// inside an asynchronous context — that panic is the enforcement, and it fires
/// on the first request.
///
/// `list_rules` and `list_bans` take no lock, and they are the silent half of
/// this: awaited on a runtime worker they would spawn `nft` and read a file
/// there, stalling every other in-flight command, with the first symptom an
/// unrelated timeout under load and nothing naming the cause. Nothing at run
/// time can announce that mistake — a blocking-pool thread and a worker are
/// indistinguishable through tokio's public API, and `ops`'s attempt to assert
/// otherwise is what made every debug build answer `/firewall` with a 500.
/// What holds it now is the call-site assertion in this file's own tests,
/// which reads this source and requires every one of the six operations to be
/// reached through `wire::run_blocking` under this area's own noun phrase.
/// The wrapper is used for all six because the requirement is the same one for
/// all six, and a handler that had to remember which kind it was calling is a
/// handler that will get it wrong.
///
/// The distro adapter is held because every operation spawns `nft`, whose
/// absolute path is a platform fact. The service asks no question of it itself
/// and branches on nothing.
pub struct FirewallServiceImpl<H> {
    /// The machine the firewall operations read and change.
    host: Arc<H>,
    /// Where `nft` lives on this family.
    distro: &'static dyn DistroAdapter,
}

impl<H: FirewallHost + 'static> FirewallServiceImpl<H> {
    /// Creates the service around the host it runs operations against.
    #[must_use]
    pub fn new(host: H, distro: &'static dyn DistroAdapter) -> Self {
        Self {
            host: Arc::new(host),
            distro,
        }
    }
}

#[tonic::async_trait]
impl<H: FirewallHost + 'static> FirewallService for FirewallServiceImpl<H> {
    /// Lists the rules the agent's own ruleset file records.
    async fn list_rules(
        &self,
        request: Request<ListRulesRequest>,
    ) -> Result<Response<ListRulesResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_ports(&request.ssh_ports, request.panel_port) {
            Ok(ports) => {
                let host = Arc::clone(&self.host);
                run_blocking("firewall operation", to_agent_error, move || {
                    firewall::list_rules(host.as_ref(), &ports)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(rules) => list_rules_response::Result::Ok(ListRulesOk {
                rules: rules.iter().map(listed_rule).collect(),
            }),
            Err(error) => list_rules_response::Result::Error(error),
        };

        Ok(Response::new(ListRulesResponse {
            result: Some(result),
        }))
    }

    /// Adds one allow rule and re-applies the whole ruleset.
    async fn allow_port(
        &self,
        request: Request<AllowPortRequest>,
    ) -> Result<Response<AllowPortResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_rule(
            request.port,
            request.protocol,
            &request.source_cidr,
            &request.ssh_ports,
            request.panel_port,
        ) {
            Ok((ports, rule)) => {
                let host = Arc::clone(&self.host);
                let distro = self.distro;

                run_blocking("firewall operation", to_agent_error, move || {
                    firewall::allow_port(host.as_ref(), distro, &ports, &rule)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => allow_port_response::Result::Ok(AllowPortOk {}),
            Err(error) => allow_port_response::Result::Error(error),
        };

        Ok(Response::new(AllowPortResponse {
            result: Some(result),
        }))
    }

    /// Removes one allow rule and re-applies the whole ruleset.
    async fn deny_port(
        &self,
        request: Request<DenyPortRequest>,
    ) -> Result<Response<DenyPortResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_rule(
            request.port,
            request.protocol,
            &request.source_cidr,
            &request.ssh_ports,
            request.panel_port,
        ) {
            Ok((ports, rule)) => {
                let host = Arc::clone(&self.host);
                let distro = self.distro;

                run_blocking("firewall operation", to_agent_error, move || {
                    firewall::deny_port(host.as_ref(), distro, &ports, &rule)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => deny_port_response::Result::Ok(DenyPortOk {}),
            Err(error) => deny_port_response::Result::Error(error),
        };

        Ok(Response::new(DenyPortResponse {
            result: Some(result),
        }))
    }

    /// Drops everything from one address until the ban expires.
    async fn ban_address(
        &self,
        request: Request<BanAddressRequest>,
    ) -> Result<Response<BanAddressResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_ban(&request.address, request.duration_seconds) {
            Ok((address, lifetime)) => {
                let host = Arc::clone(&self.host);
                let distro = self.distro;

                run_blocking("firewall operation", to_agent_error, move || {
                    firewall::ban_address(host.as_ref(), distro, &address, lifetime)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => ban_address_response::Result::Ok(BanAddressOk {}),
            Err(error) => ban_address_response::Result::Error(error),
        };

        Ok(Response::new(BanAddressResponse {
            result: Some(result),
        }))
    }

    /// Lifts a ban.
    async fn unban_address(
        &self,
        request: Request<UnbanAddressRequest>,
    ) -> Result<Response<UnbanAddressResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_address(&request.address) {
            Ok(address) => {
                let host = Arc::clone(&self.host);
                let distro = self.distro;

                run_blocking("firewall operation", to_agent_error, move || {
                    firewall::unban_address(host.as_ref(), distro, &address)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => unban_address_response::Result::Ok(UnbanAddressOk {}),
            Err(error) => unban_address_response::Result::Error(error),
        };

        Ok(Response::new(UnbanAddressResponse {
            result: Some(result),
        }))
    }

    /// Lists the bans the kernel is currently holding.
    async fn list_bans(
        &self,
        _request: Request<ListBansRequest>,
    ) -> Result<Response<ListBansResponse>, Status> {
        let host = Arc::clone(&self.host);
        let distro = self.distro;
        let result = run_blocking("firewall operation", to_agent_error, move || {
            firewall::list_bans(host.as_ref(), distro)
        })
        .await;

        let result = match result {
            Ok(bans) => list_bans_response::Result::Ok(ListBansOk {
                bans: bans.iter().map(listed_ban).collect(),
            }),
            Err(error) => list_bans_response::Result::Error(error),
        };

        Ok(Response::new(ListBansResponse {
            result: Some(result),
        }))
    }
}

#[cfg(test)]
#[path = "../../tests/services/firewall/firewall_service_tests.rs"]
mod tests;
