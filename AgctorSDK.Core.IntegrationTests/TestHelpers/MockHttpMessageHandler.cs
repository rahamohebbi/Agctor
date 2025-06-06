using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.IntegrationTests.TestHelpers
{
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly HttpContent _content;

        public MockHttpMessageHandler(HttpStatusCode statusCode, HttpContent content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage
            {
                StatusCode = _statusCode,
                Content = _content
            };
            return Task.FromResult(response);
        }
    }
} 