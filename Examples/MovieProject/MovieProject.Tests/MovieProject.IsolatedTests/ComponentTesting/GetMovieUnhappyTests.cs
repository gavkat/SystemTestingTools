using FluentAssertions;
using FluentAssertions.Execution;
using MovieProject.Web;

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using SystemTestingTools;
using Xunit;

namespace IsolatedTests.ComponentTestings
{
    [Collection("SharedServer collection")]
    [Trait("Project", "MovieProject Component Tests (Unhappy)")]
    public class GetMovieUnhappyTests
    {
        private readonly TestServerFixture Fixture;

        private static string MovieUrl = "http://www.omdbapi.com/?apikey=863d6589&type=movie";
        private static string MatrixMovieUrl = $"{MovieUrl}&t=matrix";

        public GetMovieUnhappyTests(TestServerFixture fixture)
        {
            Fixture = fixture;
        }

        [Fact]
        public async Task When_CallThrowsException_Then_LogError_And_Return_Downstream_Error()
        {
            // arrange
            var client = Fixture.Server.CreateClient();
            client.CreateSession();
            var exception = new HttpRequestException("weird network error");
            // add exception twice because we configured a retry
            client.AppendHttpCallStub(HttpMethod.Get, new System.Uri(MatrixMovieUrl), exception, counter: 2);

            // act
            var httpResponse = await client.GetAsync("/api/movie/matrix");

            using (new AssertionScope())
            {
                // assert logs
                var logs = client.GetSessionLogs();
                logs.Count.Should().Be(1);
                logs[0].ToString().Should().StartWith($"Critical: GET {MatrixMovieUrl} threw exception [weird network error]");

                // assert outgoing            
                var outgoingRequests = client.GetSessionOutgoingRequests();
                outgoingRequests.Count.Should().Be(2); // because of retry

                AssertMatrixEndpointCalled(outgoingRequests[0]);
                AssertMatrixEndpointCalled(outgoingRequests[1]);

                // assert return
                httpResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
                var message = await httpResponse.ReadBody();
                message.Should().Be(Constants.DownstreamErrorMessage);
            }
        }

        [InlineData(HttpStatusCode.TooManyRequests, "Fake_Responses/Unhappy/429_TooManyRequests_ProperlyFormatted.txt", "Too many requests with your api key")]
        [InlineData(HttpStatusCode.TooManyRequests, "Fake_Responses/Unhappy/429_TooManyRequests_RawFormatted.txt", "Too many requests with your api key")]
        [InlineData(HttpStatusCode.InternalServerError, "Fake_Responses/Unhappy/500_InternalServerError.txt", "<title>Internal Server Error</title>")]
        [InlineData(HttpStatusCode.ServiceUnavailable, "Fake_Responses/Unhappy/503_ServiceUnailable.txt", "The service is temporarily unavailable")]
        [InlineData(HttpStatusCode.OK, "Real_Responses/Unhappy/200_SomethingWentWrong_WhenSendingEmptyMovieName.txt", "Something went wrong")]
        [InlineData(HttpStatusCode.Unauthorized, "Real_Responses/Unhappy/401_InvalidKey.txt", "Invalid API key!")]
        [InlineData(HttpStatusCode.Unauthorized, "Real_Responses/Unhappy/401_LimitReached.txt", "Request limit reached")]
        [InlineData(HttpStatusCode.RequestTimeout, "Fake_Responses/Unhappy/408_Timeout.txt", " ")]
        [Theory]
        public async Task When_DownstreamSystemReturnsError_Then_LogError_And_ReturnDefaultErrorMessage(HttpStatusCode httpStatus, string fileName, string logContent)
        {
            // arrange
            // errors that are worth retrying
            bool isTransientDownstreamError = (int)httpStatus >= 500 || httpStatus == HttpStatusCode.RequestTimeout;

            var client = Fixture.Server.CreateClient();
            client.CreateSession();
            var response = ResponseFactory.FromFiddlerLikeResponseFile($"{Fixture.StubsFolder}/OmdbApi/{fileName}");
            // we add it twice to account for the recall attempt
            client.AppendHttpCallStub(HttpMethod.Get, new System.Uri(MatrixMovieUrl), response);
            if(isTransientDownstreamError) client.AppendHttpCallStub(HttpMethod.Get, new System.Uri(MatrixMovieUrl), response);

            // act
            var httpResponse = await client.GetAsync("/api/movie/matrix");

            using (new AssertionScope())
            {
                // assert logs
                var logs = client.GetSessionLogs();
                logs.Count.Should().Be(1);
                // we check that we log downstream errors specifically with extra details so we can easily debug, the format should be
                // Critical: URL returned invalid response: http status=XXX and body [FULL RESPONSE BODY HERE]
                logs[0].ToString().Should().StartWith($"Critical: GET {MatrixMovieUrl} had exception");
                logs[0].Message.Should().Contain(logContent);
                logs[0].Message.Should().Contain($" response HttpStatus={(int)httpStatus} and body=[");

                // assert outgoing            
                var outgoingRequests = client.GetSessionOutgoingRequests();
                outgoingRequests.Count.Should().Be(isTransientDownstreamError ? 2 : 1); // 2 calls because we configured a retry
                AssertMatrixEndpointCalled(outgoingRequests[0]);

                if (isTransientDownstreamError)
                {
                    AssertMatrixEndpointCalled(outgoingRequests[1]);
                }

                // assert return
                httpResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
                var message = await httpResponse.ReadBody();
                message.Should().Be(Constants.DownstreamErrorMessage);
            }
        }

        [Fact]
        public async Task When_DownstreamSystemReturnsInvalidJson_Then_LogError_And_ReturnDefaultErrorMessage()
        {
            // arrange
            var client = Fixture.Server.CreateClient();
            client.CreateSession();
            var response = ResponseFactory.FromFiddlerLikeResponseFile($"{Fixture.StubsFolder}/OmdbApi/Fake_Responses/Unhappy/200_Unexpected_Json.txt");
            // we add it twice to account for the recall attempt
            client.AppendHttpCallStub(HttpMethod.Get, new System.Uri(MatrixMovieUrl), response);

            // act
            var httpResponse = await client.GetAsync("/api/movie/matrix");

            using (new AssertionScope())
            {
                // assert logs
                var logs = client.GetSessionLogs();
                logs.Count.Should().Be(1);
                // we check that we log downstream errors specifically with extra details so we can easily debug, the format should be
                // Critical: URL returned invalid response: http status=XXX and body [FULL RESPONSE BODY HERE]
                logs[0].ToString().Should().StartWith($"Critical: GET {MatrixMovieUrl} had exception [DTO is invalid] while [processing response], response HttpStatus=200 and body=[");
                logs[0].Message.Should().Contain(@"""weirdRoot"":");

                // assert outgoing            
                var outgoingRequests = client.GetSessionOutgoingRequests();
                AssertMatrixEndpointCalled(outgoingRequests[0]);

                // assert return
                httpResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
                var message = await httpResponse.ReadBody();
                message.Should().Be(Constants.DownstreamErrorMessage);
            }
        }



        [InlineData("movie/    a", "name has too few characters", "a")]
        [InlineData("movie/1    ", "name has too few characters", "1")]
        [Theory]
        public async Task When_UserDoesntSendNecessaryInput_Then_LogWarning_And_ReturnBadRequestMessage(string route, string correctMessage, string culprit)
        {
            // arrange
            var client = Fixture.Server.CreateClient();
            client.CreateSession();

            // act
            var httpResponse = await client.GetAsync($"/api/{route}");

            // assert logs
            using (new AssertionScope())
            {
                var logs = client.GetSessionLogs();
                logs.Count.Should().Be(1);
                logs[0].ToString().Should().Be($"Warning: Bad request={correctMessage} = [{culprit}]");

                // assert outgoing
                client.GetSessionOutgoingRequests().Count.Should().Be(0);

                // assert return
                httpResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                var message = await httpResponse.ReadBody();
                message.Should().Be($"{correctMessage} = [{culprit}]");
            }
        }

        private void AssertMatrixEndpointCalled(HttpRequestMessage request)
        {
            request.GetEndpoint().Should().Be($"GET {MatrixMovieUrl}");
            request.GetHeaderValue("Referer").Should().Be(MovieProject.Logic.Constants.Website);
        }
    }
}
