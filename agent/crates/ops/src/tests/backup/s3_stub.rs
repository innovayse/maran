//! A local HTTP server that answers enough of the S3 API to test against.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::HashMap;
use std::io::{BufRead as _, BufReader, Read as _, Write as _};
use std::net::{Shutdown, TcpListener, TcpStream};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::thread::JoinHandle;

/// A tiny S3-compatible HTTP server for one test.
///
/// # What it is for, and what it categorically is not
///
/// It exists so a test can assert two things about this product's code: the
/// SHAPE of the request it sends (method, path, whether it carried an
/// `Authorization` header) and the way it TRANSLATES a response into a verdict
/// or an error. Both of those are ours, and both are testable here.
///
/// **It proves nothing whatsoever about a real provider's policy semantics.**
/// It returns the status code the test told it to return. Whether Amazon,
/// Cloudflare, Backblaze or MinIO actually answers an unauthenticated GET of a
/// private object with 403 rather than 404, whether a bucket-level public-access
/// block overrides an object ACL, whether a CDN in front of the bucket serves
/// the object regardless — none of that is knowable from a stub, because a stub
/// is this product's own idea of what a provider does, and the whole reason the
/// public-read probe exists is that this product's idea of a bucket's policy is
/// not evidence. That answer comes from the live pass against real providers.
///
/// So: green here means "we send the right request and read the answer
/// correctly". It does not mean "the probe works".
///
/// # Shape
///
/// One thread accepts connections until [`Self::drop`]; each connection is
/// served on its own thread and speaks HTTP/1.1 with `Connection: close`.
/// Objects live in memory. Multipart upload is deliberately NOT implemented —
/// no test here uploads a file larger than one part, and a stub of the
/// multipart protocol would be a second implementation of it whose agreement
/// with any real provider is exactly as unproven as everything else here.
pub(crate) struct S3Stub {
    /// Where the server is listening, as an `http://host:port` endpoint.
    endpoint: String,

    /// One line per request served: `"<METHOD> <target> auth=<yes|no>"`.
    requests: Arc<Mutex<Vec<String>>>,

    /// The objects the stub holds, keyed by request path.
    objects: Arc<Mutex<HashMap<String, Vec<u8>>>>,

    /// Set when the stub is dropped, so the accept loop stops.
    stopping: Arc<AtomicBool>,

    /// The accept loop, joined on drop.
    accepting: Option<JoinHandle<()>>,
}

impl S3Stub {
    /// Starts a stub that answers a GET of a stored object with `get_status`.
    ///
    /// `get_status` is the only configurable answer, because it is the only one
    /// the tests vary: 200 is a bucket serving objects to the world, 403 and 404
    /// are a bucket refusing, and 500 is a provider that answered without
    /// answering the question.
    pub(crate) fn start(get_status: u16) -> Self {
        let listener = TcpListener::bind("127.0.0.1:0").expect("a loopback port");
        let address = listener.local_addr().expect("the bound address");
        listener
            .set_nonblocking(true)
            .expect("the listener accepts non-blocking mode");

        let requests = Arc::new(Mutex::new(Vec::new()));
        let objects = Arc::new(Mutex::new(HashMap::new()));
        let stopping = Arc::new(AtomicBool::new(false));

        let accepting = {
            let requests = Arc::clone(&requests);
            let objects = Arc::clone(&objects);
            let stopping = Arc::clone(&stopping);
            std::thread::spawn(move || {
                while !stopping.load(Ordering::Relaxed) {
                    match listener.accept() {
                        Ok((stream, _)) => {
                            let requests = Arc::clone(&requests);
                            let objects = Arc::clone(&objects);
                            std::thread::spawn(move || {
                                serve(&stream, &requests, &objects, get_status);
                                let _ = stream.shutdown(Shutdown::Both);
                            });
                        }
                        Err(error) if error.kind() == std::io::ErrorKind::WouldBlock => {
                            std::thread::yield_now();
                        }
                        Err(_) => break,
                    }
                }
            })
        };

        Self {
            endpoint: format!("http://{address}"),
            requests,
            objects,
            stopping,
            accepting: Some(accepting),
        }
    }

    /// The `http://host:port` this stub is listening on.
    pub(crate) fn endpoint(&self) -> &str {
        &self.endpoint
    }

    /// One line per request served, oldest first.
    pub(crate) fn requests(&self) -> Vec<String> {
        self.requests
            .lock()
            .expect("the recorder is not poisoned")
            .clone()
    }

    /// Puts an object into the stub without going through a request.
    pub(crate) fn insert(&self, path: &str, bytes: &[u8]) {
        self.objects
            .lock()
            .expect("the store is not poisoned")
            .insert(path.to_owned(), bytes.to_vec());
    }

    /// Whether the stub holds an object at `path`.
    pub(crate) fn holds(&self, path: &str) -> bool {
        self.objects
            .lock()
            .expect("the store is not poisoned")
            .contains_key(path)
    }
}

impl Drop for S3Stub {
    /// Stops the accept loop and joins it, so no thread outlives the test.
    fn drop(&mut self) {
        self.stopping.store(true, Ordering::Relaxed);
        if let Some(accepting) = self.accepting.take() {
            let _ = accepting.join();
        }
    }
}

/// Serves every request on one connection.
fn serve(
    stream: &TcpStream,
    requests: &Arc<Mutex<Vec<String>>>,
    objects: &Arc<Mutex<HashMap<String, Vec<u8>>>>,
    get_status: u16,
) {
    let mut reader = BufReader::new(stream);
    let Some((method, target, headers)) = read_head(&mut reader) else {
        return;
    };

    let authorised = headers.contains_key("authorization");
    requests
        .lock()
        .expect("the recorder is not poisoned")
        .push(format!(
            "{method} {target} auth={}",
            if authorised { "yes" } else { "no" }
        ));

    let length: usize = headers
        .get("content-length")
        .and_then(|value| value.parse().ok())
        .unwrap_or(0);
    let mut body = vec![0_u8; length];
    if length > 0 && reader.read_exact(&mut body).is_err() {
        return;
    }

    let (path, query) = match target.split_once('?') {
        Some((path, query)) => (path, query),
        None => (target.as_str(), ""),
    };

    let mut ranged: Option<String> = None;
    let mut held = objects.lock().expect("the store is not poisoned");
    let response = match method.as_str() {
        "PUT" => {
            held.insert(path.to_owned(), body);
            (200_u16, Vec::new())
        }
        "DELETE" => {
            if held.remove(path).is_some() {
                (204, Vec::new())
            } else {
                (404, Vec::new())
            }
        }
        "HEAD" => match held.get(path) {
            Some(bytes) => (200, vec![0_u8; bytes.len()]),
            None => (404, Vec::new()),
        },
        // A single `delete` goes out as a bulk-delete POST, which is the
        // transport's choice and not this product's; the stub answers the
        // request that is actually sent rather than the one we expected.
        "POST" if query.contains("delete") => {
            let body = String::from_utf8_lossy(&body).into_owned();
            let mut deleted = String::new();
            for key in keys_of(&body) {
                held.remove(&format!("{path}/{key}"));
                deleted.push_str(&format!("<Deleted><Key>{key}</Key></Deleted>"));
            }
            (
                200,
                format!(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\
                     <DeleteResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">\
                     {deleted}</DeleteResult>"
                )
                .into_bytes(),
            )
        }
        "GET" if query.contains("list-type") => (200, listing(&held, query).into_bytes()),
        "GET" => match held.get(path) {
            // A ranged GET must answer 206 with the slice: this product
            // downloads an artifact one range at a time rather than holding
            // sixty gigabytes of a customer's archive in memory, and a stub
            // that answered the whole object would be testing a request this
            // code does not send.
            Some(bytes) if get_status == 200 => match headers.get("range").map(String::as_str) {
                Some(range) => {
                    let (first, last) = requested_range(range, bytes.len());
                    ranged = Some(format!("bytes {first}-{last}/{}", bytes.len()));
                    (206, bytes[first..=last].to_vec())
                }
                None => (200, bytes.clone()),
            },
            Some(_) => (get_status, b"<Error><Code>Stub</Code></Error>".to_vec()),
            None => (404, Vec::new()),
        },
        _ => (405, Vec::new()),
    };
    drop(held);

    write_response(
        stream,
        response.0,
        method == "HEAD",
        ranged.as_deref(),
        &response.1,
    );
}

/// The inclusive byte range a `bytes=first-last` header asks for, clamped to
/// the object.
fn requested_range(header: &str, length: usize) -> (usize, usize) {
    let value = header.trim().strip_prefix("bytes=").unwrap_or("");
    let (first, last) = value.split_once('-').unwrap_or((value, ""));
    let first = first
        .parse::<usize>()
        .unwrap_or(0)
        .min(length.saturating_sub(1));
    let last = last
        .parse::<usize>()
        .unwrap_or(length.saturating_sub(1))
        .min(length.saturating_sub(1));

    (first, last.max(first))
}

/// Every `<Key>` a bulk-delete body names.
fn keys_of(body: &str) -> Vec<String> {
    body.split("<Key>")
        .skip(1)
        .filter_map(|rest| rest.split_once("</Key>"))
        .map(|(key, _)| key.to_owned())
        .collect()
}

/// Reads the request line and headers, lowercasing header names.
fn read_head(
    reader: &mut BufReader<&TcpStream>,
) -> Option<(String, String, HashMap<String, String>)> {
    let mut line = String::new();
    if reader.read_line(&mut line).ok()? == 0 {
        return None;
    }
    let mut parts = line.split_whitespace();
    let method = parts.next()?.to_owned();
    let target = parts.next()?.to_owned();

    let mut headers = HashMap::new();
    loop {
        let mut header = String::new();
        if reader.read_line(&mut header).ok()? == 0 {
            break;
        }
        let header = header.trim_end();
        if header.is_empty() {
            break;
        }
        if let Some((name, value)) = header.split_once(':') {
            headers.insert(name.trim().to_ascii_lowercase(), value.trim().to_owned());
        }
    }

    Some((method, target, headers))
}

/// Renders a `ListBucketResult` naming every held object under the query's
/// prefix.
fn listing(held: &HashMap<String, Vec<u8>>, query: &str) -> String {
    let prefix = query
        .split('&')
        .find_map(|pair| pair.strip_prefix("prefix="))
        .unwrap_or_default()
        .replace("%2F", "/");

    let mut keys: Vec<(String, usize)> = held
        .iter()
        .filter_map(|(path, bytes)| {
            let key = path.split('/').skip(2).collect::<Vec<_>>().join("/");
            key.starts_with(&prefix).then_some((key, bytes.len()))
        })
        .collect();
    keys.sort();

    let contents: String = keys
        .into_iter()
        .map(|(key, length)| {
            format!(
                "<Contents><Key>{key}</Key><Size>{length}</Size>\
                 <LastModified>1970-01-01T00:00:00.000Z</LastModified>\
                 <ETag>\"stub\"</ETag></Contents>"
            )
        })
        .collect();

    format!(
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\
         <ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">\
         <IsTruncated>false</IsTruncated>{contents}</ListBucketResult>"
    )
}

/// Writes one HTTP/1.1 response and closes the connection.
fn write_response(
    mut stream: &TcpStream,
    status: u16,
    head_only: bool,
    content_range: Option<&str>,
    body: &[u8],
) {
    let range =
        content_range.map_or_else(String::new, |range| format!("Content-Range: {range}\r\n"));
    let head = format!(
        "HTTP/1.1 {status} Stub\r\n\
         {range}\
         Content-Length: {}\r\n\
         Content-Type: application/xml\r\n\
         ETag: \"stub\"\r\n\
         Last-Modified: Thu, 01 Jan 1970 00:00:00 GMT\r\n\
         Connection: close\r\n\r\n",
        body.len()
    );
    let _ = stream.write_all(head.as_bytes());
    if !head_only {
        let _ = stream.write_all(body);
    }
    let _ = stream.flush();
}
