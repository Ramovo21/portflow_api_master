using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using PortFlow.Tests.Support;

namespace PortFlow.Tests.Shared.Attachment;

public class AttachmentIT(IntegrationFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task UploadStoresFileWithHashAndDownloadUrlServesSameBytes()
    {
        var content = Encoding.UTF8.GetBytes("%PDF-1.4 hợp đồng mẫu");
        var expectedSha = Convert.ToHexStringLower(SHA256.HashData(content));

        var response = await Http.SendAsync(Upload("TEST_DOC", "hop-dong.pdf", "application/pdf", content));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(expectedSha, body.GetProperty("sha256").GetString());
        Assert.Equal(content.Length, body.GetProperty("size").GetInt64());
        Assert.False(body.GetProperty("locked").GetBoolean());

        var id = body.GetProperty("id").GetString();
        var urlResponse = await Http.GetAsync($"/api/v1/attachments/{id}/download-url");
        Assert.Equal(HttpStatusCode.OK, urlResponse.StatusCode);
        var url = (await ReadJsonAsync(urlResponse)).GetProperty("url").GetString();

        using var http = new HttpClient();
        var downloaded = await http.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal(content, await downloaded.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task RejectsDisallowedFileType()
    {
        var response = await Http.SendAsync(Upload("TEST_DOC", "virus.exe", "application/octet-stream", [1, 2, 3]));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("SYS_FILE_TYPE_NOT_ALLOWED", (await ReadJsonAsync(response)).GetProperty("errorCode").GetString());

        response = await Http.SendAsync(Upload("TEST_DOC", "virus.exe", "application/pdf", [1, 2, 3]));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task RejectsFilesOver25Megabytes()
    {
        var big = new byte[25 * 1024 * 1024 + 1];
        var response = await Http.SendAsync(Upload("TEST_DOC", "big.pdf", "application/pdf", big));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("SYS_FILE_TOO_LARGE", (await ReadJsonAsync(response)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task RejectsEntityTypeWithoutAccessPolicy()
    {
        var response = await Http.SendAsync(Upload("UNKNOWN", "a.pdf", "application/pdf", [1]));
        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);
        Assert.Equal("SYS_ATTACHMENT_ENTITY_UNSUPPORTED", (await ReadJsonAsync(response)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task RolledBackUploadLeavesNoRecord()
    {
        var before = await CountAsync("SELECT count(*) FROM shared.attachment");
        var response = await Http.SendAsync(Upload("UNKNOWN", "a.pdf", "application/pdf", [1]));
        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);
        Assert.Equal(before, await CountAsync("SELECT count(*) FROM shared.attachment"));
    }

    private static HttpRequestMessage Upload(string entityType, string fileName, string contentType, byte[] content)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        var form = new MultipartFormDataContent
        {
            { new StringContent(entityType), "entityType" },
            { new StringContent(Guid.NewGuid().ToString()), "entityId" },
            { file, "file", fileName },
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/attachments") { Content = form };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return request;
    }
}
