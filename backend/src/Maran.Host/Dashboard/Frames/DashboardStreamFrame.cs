namespace Maran.Host.Dashboard.Frames;

/// <summary>
/// One event of the live dashboard stream: its name on the wire and its JSON payload. Every kind of
/// event is a record deriving from this, so the set of events is closed and a new one must be named.
/// </summary>
/// <remarks>
/// The payload is the record itself unless a kind says otherwise, so the wire shape of an event is
/// exactly the shape of its type. The two members below are METHODS and not properties on purpose: the
/// serializer writes every public property of the payload, and a property here would put the event's
/// own name into its data.
/// </remarks>
public abstract record DashboardStreamFrame
{
    /// <summary>The SSE <c>event:</c> name the SPA dispatches on.</summary>
    public abstract string EventName();

    /// <summary>The value serialized as the event's <c>data:</c>; the frame itself by default.</summary>
    public virtual object ToPayload()
    {
        return this;
    }
}
