using System.Net;

namespace DocViewer.Infrastructure.Sources;

/// <summary>A source answered with an HTTP status the adapter treats as failure.</summary>
public sealed class SourceHttpException : Exception
{
    public SourceHttpException()
    {
    }

    public SourceHttpException(string message)
        : base(message)
    {
    }

    public SourceHttpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SourceHttpException(HttpStatusCode statusCode)
        : base($"Source responded with HTTP {(int)statusCode}.") => StatusCode = statusCode;

    public HttpStatusCode StatusCode { get; }
}
