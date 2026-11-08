//! The `SitesService` handler, its error mapping, and the checks that turn a
//! proto site description into a validated operation input.

pub mod site_status;
pub mod sites_service;
pub mod stream_log_sink;
pub mod tail_terminal;
pub mod to_wire_traffic_summary;
pub mod validated_existing_identity;
pub mod validated_existing_site;
pub mod validated_identity;
pub mod validated_options;
pub mod validated_overrides;
pub mod validated_prune_request;
pub mod validated_site;
pub mod validated_traffic_request;
