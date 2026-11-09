using Maran.Modules.Files.Controllers;
using Maran.Modules.Files.Models;
using Microsoft.AspNetCore.Http;

namespace Maran.Modules.Files.Tests.Controllers;

/// <summary>That a download never reaches a browser as anything but an attachment of opaque bytes.</summary>
public sealed class FilesTransferResponseTests
{
    /// <summary>Prepares a response for a download of a file with this name and size.</summary>
    /// <param name="fileName">The proposed name.</param>
    /// <param name="size">The file's size.</param>
    /// <returns>The prepared response.</returns>
    private static HttpResponse Prepared(string fileName, ulong size = 7)
    {
        var context = new DefaultHttpContext();
        var download = new FileDownload(fileName, size, (_, _) => { return Task.CompletedTask; }, () => { return ValueTask.CompletedTask; });

        FilesTransferResponse.PrepareDownload(context.Response, download);

        return context.Response;
    }

    /// <summary>Every download is opaque bytes: the type is never the file's own, so a page or an image is never rendered.</summary>
    [Theory]
    [InlineData("page.html")]
    [InlineData("image.svg")]
    [InlineData("script.js")]
    public void A_download_is_always_octet_stream_and_an_attachment_whatever_the_file_is(string fileName)
    {
        var response = Prepared(fileName);

        Assert.Equal("application/octet-stream", response.ContentType);
        Assert.StartsWith("attachment;", response.Headers.ContentDisposition.ToString());
        Assert.Equal("nosniff", response.Headers.XContentTypeOptions.ToString());
        Assert.Equal("no-store", response.Headers.CacheControl.ToString());
    }

    /// <summary>The response declares the length the agent listed, so a short body is detectable by the browser.</summary>
    [Fact]
    public void The_response_declares_the_length_the_file_was_listed_with()
    {
        Assert.Equal(7, Prepared("a.bin", 7).ContentLength);
    }

    /// <summary>A name carrying a quote or a non-ASCII character cannot break out of the header: the plain name is safe and the real one is encoded.</summary>
    [Fact]
    public void A_name_cannot_break_out_of_the_disposition_header()
    {
        var response = Prepared("na\"me é.txt");

        var disposition = response.Headers.ContentDisposition.ToString();
        Assert.Equal("attachment; filename=\"na_me _.txt\"; filename*=UTF-8''na%22me%20%C3%A9.txt", disposition);
        Assert.DoesNotContain("\r", disposition);
        Assert.DoesNotContain("\n", disposition);
    }
}
