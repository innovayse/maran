//! Readings the monitoring operations return, and the parsed shapes they are
//! built from — one type per file (rules/rust.md "Operation anatomy").

pub mod account_disk_usage;
pub mod cpu_times;
pub mod filesystem_usage;
pub mod host_metrics;
pub mod load_average;
pub mod memory_usage;
pub mod network_counters;
pub mod service_state;
pub mod service_status;
pub mod unit_report;
