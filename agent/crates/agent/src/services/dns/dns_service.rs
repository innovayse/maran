//! `DnsService`: this panel's own authoritative name server.

use std::path::Path;
use std::sync::Arc;

use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_ops::dns::{self, ZoneHost, ZoneReading};
use tonic::{Request, Response, Status};

use crate::audit::audit_slot::AuditSlot;
use crate::proto::dns_service_server::DnsService;
use crate::proto::{
    DeleteZoneOk, DeleteZoneRequest, DeleteZoneResponse, DisableDnsOk, DisableDnsRequest,
    DisableDnsResponse, EnableDnsOk, EnableDnsRequest, EnableDnsResponse, GetDnsStatusOk,
    GetDnsStatusRequest, GetDnsStatusResponse, GetZoneOk, GetZoneRequest, GetZoneResponse,
    ListZonesOk, ListZonesRequest, ListZonesResponse, PutZoneOk, PutZoneRequest, PutZoneResponse,
    delete_zone_response, disable_dns_response, enable_dns_response, get_dns_status_response,
    get_zone_response, list_zones_response, put_zone_response,
};
use crate::services::dns::dns_status::to_agent_error;
use crate::services::dns::to_wire_dns_status::to_wire_dns_status;
use crate::services::dns::validated_zone::validated_zone;
use crate::services::dns::validated_zone_name::validated_zone_name;
use crate::services::wire::run_blocking::run_blocking;

/// Serves the DNS daemon over the wire.
///
/// Every implemented rpc follows the same three steps: take the request, run one
/// operation, map the outcome into the response's `oneof`. Failures travel in the
/// payload rather than as a gRPC status, because they are answers the panel acts
/// on (rules/proto.md). None of the three carries a value from the request: the
/// daemon's configuration is rendered from agent constants and distro facts only.
///
/// The four zone rpcs carry validated values and nothing else: the zone name, the
/// start-of-authority data and the records are rebuilt through the validated types
/// of `maran-agent-core`, and no rpc takes a configuration fragment, a record in
/// text form or a path. Zone files are root-owned data in the zone directory the
/// distribution's facts name, so no customer path is involved.
pub struct DnsServiceImpl<H> {
    /// The machine the DNS operations run against.
    host: Arc<H>,
    /// Where platform facts come from: the checker, the control client, the
    /// service manager. A service never branches on a distribution itself
    /// (rules/rust.md "Distro adapter"); it passes this on.
    distro: &'static dyn maran_distro::DistroAdapter,
    /// The free-space floor a zone write is refused below.
    floor: FreeSpaceFloor,
}

impl<H: ZoneHost + 'static> DnsServiceImpl<H> {
    /// Creates the service around the host it runs operations against.
    #[must_use]
    pub fn new(
        host: H,
        distro: &'static dyn maran_distro::DistroAdapter,
        floor: FreeSpaceFloor,
    ) -> Self {
        Self {
            host: Arc::new(host),
            distro,
            floor,
        }
    }
}

#[tonic::async_trait]
impl<H: ZoneHost + 'static> DnsService for DnsServiceImpl<H> {
    /// Renders, checks and applies the daemon's configuration, then brings it up
    /// — and puts the previous configuration back if it does not answer.
    async fn enable_dns(
        &self,
        request: Request<EnableDnsRequest>,
    ) -> Result<Response<EnableDnsResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let (host, distro) = (Arc::clone(&self.host), self.distro);
        let result = run_blocking("dns operation", to_agent_error, move || {
            dns::enable_dns(host.as_ref(), distro)
        })
        .await;

        audit.settle(&result);
        let result = match result {
            Ok(state) => enable_dns_response::Result::Ok(EnableDnsOk {
                status: Some(to_wire_dns_status(&state)),
            }),
            Err(error) => enable_dns_response::Result::Error(error),
        };

        Ok(Response::new(EnableDnsResponse {
            result: Some(result),
        }))
    }

    /// Stops the daemon and takes it out of the boot sequence, leaving every
    /// zone exactly as it is.
    async fn disable_dns(
        &self,
        request: Request<DisableDnsRequest>,
    ) -> Result<Response<DisableDnsResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let (host, distro) = (Arc::clone(&self.host), self.distro);
        let result = run_blocking("dns operation", to_agent_error, move || {
            dns::disable_dns(host.as_ref(), distro)
        })
        .await;

        audit.settle(&result);
        let result = match result {
            Ok(state) => disable_dns_response::Result::Ok(DisableDnsOk {
                status: Some(to_wire_dns_status(&state)),
            }),
            Err(error) => disable_dns_response::Result::Error(error),
        };

        Ok(Response::new(DisableDnsResponse {
            result: Some(result),
        }))
    }

    /// Reports what the daemon is doing, observed on the host and not read from
    /// what the panel asked for.
    async fn get_dns_status(
        &self,
        _request: Request<GetDnsStatusRequest>,
    ) -> Result<Response<GetDnsStatusResponse>, Status> {
        let (host, distro) = (Arc::clone(&self.host), self.distro);
        let result = run_blocking("dns status", to_agent_error, move || {
            dns::get_dns_status(host.as_ref(), distro)
        })
        .await;

        let result = match result {
            Ok(state) => get_dns_status_response::Result::Ok(GetDnsStatusOk {
                status: Some(to_wire_dns_status(&state)),
            }),
            Err(error) => get_dns_status_response::Result::Error(error),
        };

        Ok(Response::new(GetDnsStatusResponse {
            result: Some(result),
        }))
    }

    /// Replaces the whole content of one zone and makes the daemon serve it.
    ///
    /// Cancellation class: run to completion. The work is a file swap and a
    /// daemon command inside `spawn_blocking`, which a dropped client cannot
    /// stop; the call is idempotent, so the panel's remedy for a response it lost
    /// is to send the same zone again.
    async fn put_zone(
        &self,
        request: Request<PutZoneRequest>,
    ) -> Result<Response<PutZoneResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let request = request.into_inner();

        let result = match validated_zone(&request) {
            Ok(content) => {
                let (host, distro, floor) = (Arc::clone(&self.host), self.distro, self.floor);
                run_blocking("dns zone operation", to_agent_error, move || {
                    dns::put_zone(
                        host.as_ref(),
                        distro,
                        Path::new(distro.dns_zone_directory()),
                        &floor,
                        &content,
                    )
                })
                .await
            }
            Err(error) => Err(error),
        };

        audit.settle(&result);
        let result = match result {
            Ok(outcome) => put_zone_response::Result::Ok(PutZoneOk {
                created: outcome.created,
                serial: outcome.serial,
            }),
            Err(error) => put_zone_response::Result::Error(error),
        };

        Ok(Response::new(PutZoneResponse {
            result: Some(result),
        }))
    }

    /// Removes one zone from the daemon and deletes its file.
    ///
    /// Cancellation class: run to completion, for the reason `put_zone` gives; a
    /// repeated delete converges.
    async fn delete_zone(
        &self,
        request: Request<DeleteZoneRequest>,
    ) -> Result<Response<DeleteZoneResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let request = request.into_inner();

        let result = match validated_zone_name(&request.zone_name) {
            Ok(zone) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                run_blocking("dns zone operation", to_agent_error, move || {
                    dns::delete_zone(
                        host.as_ref(),
                        distro,
                        Path::new(distro.dns_zone_directory()),
                        &zone,
                    )
                })
                .await
            }
            Err(error) => Err(error),
        };

        audit.settle(&result);
        let result = match result {
            Ok(outcome) => delete_zone_response::Result::Ok(DeleteZoneOk {
                was_present: outcome.was_present,
            }),
            Err(error) => delete_zone_response::Result::Error(error),
        };

        Ok(Response::new(DeleteZoneResponse {
            result: Some(result),
        }))
    }

    /// Reports what the daemon serves for one zone, asked of the daemon.
    async fn get_zone(
        &self,
        request: Request<GetZoneRequest>,
    ) -> Result<Response<GetZoneResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_zone_name(&request.zone_name) {
            Ok(zone) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                run_blocking("dns zone status", to_agent_error, move || {
                    dns::get_zone(host.as_ref(), distro, &zone)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(ZoneReading::Served { serial }) => get_zone_response::Result::Ok(GetZoneOk {
                served: true,
                serial,
            }),
            Ok(ZoneReading::NotServed) => get_zone_response::Result::Ok(GetZoneOk {
                served: false,
                serial: 0,
            }),
            Err(error) => get_zone_response::Result::Error(error),
        };

        Ok(Response::new(GetZoneResponse {
            result: Some(result),
        }))
    }

    /// Lists the apex names of the zones the daemon serves.
    async fn list_zones(
        &self,
        _request: Request<ListZonesRequest>,
    ) -> Result<Response<ListZonesResponse>, Status> {
        let (host, distro) = (Arc::clone(&self.host), self.distro);
        let result = run_blocking("dns zone listing", to_agent_error, move || {
            dns::list_zones(
                host.as_ref(),
                distro,
                Path::new(distro.dns_zone_directory()),
            )
        })
        .await;

        let result = match result {
            Ok(zones) => list_zones_response::Result::Ok(ListZonesOk {
                zone_names: zones.iter().map(|zone| zone.as_str().to_owned()).collect(),
            }),
            Err(error) => list_zones_response::Result::Error(error),
        };

        Ok(Response::new(ListZonesResponse {
            result: Some(result),
        }))
    }
}
