//! One accepted connection of the agent, on either transport.

use std::io;
use std::pin::Pin;
use std::task::{Context, Poll};

use tokio::io::{AsyncRead, AsyncWrite, ReadBuf};
use tokio::net::{TcpStream, UnixStream};
use tokio_rustls::server::TlsStream;
use tonic::transport::server::Connected;

use super::agent_peer::AgentPeer;
use super::fleet_peer::FleetPeer;

/// A connection the gRPC server is handed: a unix-socket stream or the TLS stream of a fleet
/// peer that has already completed its handshake.
///
/// One type, so one router serves both listeners and every service, every process-wide lock and
/// every concurrency bound behind it exists once. The alternative, a second router, would build
/// every service twice and double each bound that is state inside a host (rules/rust.md
/// "What this agent serialises"). Reads and writes are delegated unchanged; the only thing this
/// type adds is [`AgentPeer`], which tells the guard which transport it is looking at.
#[derive(Debug)]
pub enum AgentConnection {
    /// A process on this host.
    Unix(UnixStream),
    /// A fleet peer, past its TLS handshake. Boxed: a TLS session is large and the unix
    /// variant is a descriptor.
    Fleet(Box<TlsStream<TcpStream>>),
}

impl AsyncRead for AgentConnection {
    /// Reads from whichever stream this is.
    fn poll_read(
        self: Pin<&mut Self>,
        cx: &mut Context<'_>,
        buf: &mut ReadBuf<'_>,
    ) -> Poll<io::Result<()>> {
        match self.get_mut() {
            Self::Unix(stream) => Pin::new(stream).poll_read(cx, buf),
            Self::Fleet(stream) => Pin::new(stream.as_mut()).poll_read(cx, buf),
        }
    }
}

impl AsyncWrite for AgentConnection {
    /// Writes to whichever stream this is.
    fn poll_write(
        self: Pin<&mut Self>,
        cx: &mut Context<'_>,
        buf: &[u8],
    ) -> Poll<io::Result<usize>> {
        match self.get_mut() {
            Self::Unix(stream) => Pin::new(stream).poll_write(cx, buf),
            Self::Fleet(stream) => Pin::new(stream.as_mut()).poll_write(cx, buf),
        }
    }

    /// Flushes whichever stream this is.
    fn poll_flush(self: Pin<&mut Self>, cx: &mut Context<'_>) -> Poll<io::Result<()>> {
        match self.get_mut() {
            Self::Unix(stream) => Pin::new(stream).poll_flush(cx),
            Self::Fleet(stream) => Pin::new(stream.as_mut()).poll_flush(cx),
        }
    }

    /// Shuts down whichever stream this is; a TLS stream sends its close notice.
    fn poll_shutdown(self: Pin<&mut Self>, cx: &mut Context<'_>) -> Poll<io::Result<()>> {
        match self.get_mut() {
            Self::Unix(stream) => Pin::new(stream).poll_shutdown(cx),
            Self::Fleet(stream) => Pin::new(stream.as_mut()).poll_shutdown(cx),
        }
    }

    /// Vectored writes where the underlying stream has them.
    fn poll_write_vectored(
        self: Pin<&mut Self>,
        cx: &mut Context<'_>,
        bufs: &[io::IoSlice<'_>],
    ) -> Poll<io::Result<usize>> {
        match self.get_mut() {
            Self::Unix(stream) => Pin::new(stream).poll_write_vectored(cx, bufs),
            Self::Fleet(stream) => Pin::new(stream.as_mut()).poll_write_vectored(cx, bufs),
        }
    }

    /// Whether the underlying stream writes vectored.
    fn is_write_vectored(&self) -> bool {
        match self {
            Self::Unix(stream) => stream.is_write_vectored(),
            Self::Fleet(stream) => stream.is_write_vectored(),
        }
    }
}

impl Connected for AgentConnection {
    type ConnectInfo = AgentPeer;

    /// Reports the peer in the shape its transport gives: the kernel's credentials for a unix
    /// peer, the presented certificate chain for a fleet peer.
    fn connect_info(&self) -> Self::ConnectInfo {
        match self {
            Self::Unix(stream) => AgentPeer::Unix(stream.connect_info()),
            Self::Fleet(stream) => {
                let info = stream.connect_info();
                AgentPeer::Fleet(FleetPeer::new(
                    info.get_ref().remote_addr(),
                    info.peer_certs(),
                ))
            }
        }
    }
}
