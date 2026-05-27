using System.Net;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HireBot.Core.Tests;

public class PackagingTestCaseLlmGeneratorTests
{
    [Fact]
    public void PrepareHistoryTranscript_ShouldFilterPackagingIntentAndShortUserMessages()
    {
        var messages = new[]
        {
            new HiringConversationMessageDto("1", "user", "hi", DateTimeOffset.UtcNow),
            new HiringConversationMessageDto("2", "user", "请开始生成产物包", DateTimeOffset.UtcNow),
            new HiringConversationMessageDto("3", "user", "我需要查询订单物流状态", DateTimeOffset.UtcNow),
            new HiringConversationMessageDto("4", "assistant", "好的，我来帮您查询物流。", DateTimeOffset.UtcNow)
        };

        var transcript = PackagingTestCaseLlmGenerator.PrepareHistoryTranscript(messages);

        Assert.Equal(2, transcript.Count);
        Assert.Equal("user", transcript[0].Role);
        Assert.Contains("物流", transcript[0].Content);
        Assert.Equal("assistant", transcript[1].Role);
    }

    [Fact]
    public void TryValidateTestCasesJson_WhenValidDemoStructure_ShouldReturnTrue()
    {
        var json = """
            {
              "description": "demo",
              "role": "customer_service",
              "industry": "ecommerce",
              "test_cases": [
                {
                  "test_case_id": "TC-001",
                  "scenario_name": "退货",
                  "input": { "user_request": "我要退货", "context": {} },
                  "expected_behavior_sequence": [
                    { "step": 1, "action": "安抚", "criteria": "友好" }
                  ],
                  "expected_output": {
                    "resolution": "已受理",
                    "user_satisfaction": "满意",
                    "artifacts_created": []
                  }
                }
              ]
            }
            """;

        var valid = PackagingTestCaseLlmGenerator.TryValidateTestCasesJson(json, out var normalized);

        Assert.True(valid);
        Assert.Contains("test_cases", normalized);
    }

    [Fact]
    public void TryValidateTestCasesJson_WhenMissingTestCases_ShouldReturnFalse()
    {
        var json = """{ "description": "demo", "role": "x", "industry": "y" }""";

        var valid = PackagingTestCaseLlmGenerator.TryValidateTestCasesJson(json, out _);

        Assert.False(valid);
    }

    [Fact]
    public void AppendPackagingMetadata_ShouldAddSourceAndGeneratedAt()
    {
        var json = """{ "description": "d", "role": "r", "industry": "i", "test_cases": [] }""";

        var enriched = PackagingTestCaseLlmGenerator.AppendPackagingMetadata(json, "kingcrab-history-llm");

        using var document = JsonDocument.Parse(enriched);
        var root = document.RootElement;
        Assert.Equal("kingcrab-history-llm", root.GetProperty("source").GetString());
        Assert.True(root.TryGetProperty("generated_at", out _));
    }

    [Fact]
    public async Task TryGenerateAsync_WhenLlmReturnsValidJson_ShouldSucceed()
    {
        var validPayload = """
            {
              "description": "雇佣评估",
              "role": "digital_employee",
              "industry": "general",
              "test_cases": [
                {
                  "test_case_id": "TC-001",
                  "scenario_name": "咨询业务",
                  "input": { "user_request": "请介绍业务流程", "context": {} },
                  "expected_behavior_sequence": [
                    { "step": 1, "action": "理解需求", "criteria": "准确" },
                    { "step": 2, "action": "给出方案", "criteria": "完整" }
                  ],
                  "expected_output": {
                    "resolution": "已解答",
                    "user_satisfaction": "满意",
                    "artifacts_created": []
                  }
                }
              ]
            }
            """;

        var llmResponse = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        role = "assistant",
                        content = validPayload
                    }
                }
            }
        });

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(llmResponse, Encoding.UTF8, "application/json")
        });

        var generator = CreateGenerator(handler, new Dictionary<string, string?>
        {
            ["OpenSandbox:KingCrab:LlmModel"] = "gpt-test",
            ["OpenSandbox:KingCrab:LlmEndpoint"] = "https://llm.example.com/v1",
            ["OpenSandbox:KingCrab:LlmApiKey"] = "secret"
        });

        var request = new PackagingTestCaseGenerationRequest(
            "测试模板",
            new Dictionary<string, string?> { ["business_goal"] = "提升效率" },
            [
                new HiringConversationMessageDto("1", "user", "请介绍业务流程", DateTimeOffset.UtcNow),
                new HiringConversationMessageDto("2", "assistant", "流程如下...", DateTimeOffset.UtcNow)
            ]);

        var result = await generator.TryGenerateAsync(request);

        Assert.True(result.Success);
        Assert.Contains("test_cases", result.Json);
    }

    private static PackagingTestCaseLlmGenerator CreateGenerator(
        HttpMessageHandler handler,
        IReadOnlyDictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var httpClientFactory = new StubHttpClientFactory(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://llm.example.com/")
        });

        return new PackagingTestCaseLlmGenerator(
            httpClientFactory,
            configuration,
            NullLogger<PackagingTestCaseLlmGenerator>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
