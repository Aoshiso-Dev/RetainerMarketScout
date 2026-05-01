using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RetainerMarketScout.Application.Abstractions;

namespace RetainerMarketScout.Infrastructure.ExpressVpn;

public sealed class ExpressVpnMcpClient(HttpClient httpClient) : IExpressVpnClient
{
    private const string ProtocolVersion = "2025-03-26";
    private string? _sessionId;

    public async Task<ExpressVpnConnectionResult> EnsureConnectedAsync(
        string? endpointAndLocation,
        CancellationToken cancellationToken)
    {
        var options = ExpressVpnMcpOptions.Parse(endpointAndLocation);
        var endpoint = new Uri(options.Endpoint);

        await SendJsonRpcAsync(endpoint, "initialize", new
        {
            protocolVersion = ProtocolVersion,
            capabilities = new { },
            clientInfo = new
            {
                name = "FF14 Retainer Market Scout",
                version = "1.0.0"
            }
        }, cancellationToken);

        await SendNotificationAsync(endpoint, "notifications/initialized", cancellationToken);

        var toolsResult = await SendJsonRpcAsync(endpoint, "tools/list", new { }, cancellationToken);
        var tools = ReadTools(toolsResult);
        var connectTool = FindConnectTool(tools);
        if (connectTool is null)
        {
            return new ExpressVpnConnectionResult
            {
                IsConnected = false,
                Message = "ExpressVPN MCPサーバーから接続用ツールを見つけられませんでした。"
            };
        }

        var arguments = BuildToolArguments(connectTool, options.Location);
        var callResult = await SendJsonRpcAsync(endpoint, "tools/call", new
        {
            name = connectTool.Name,
            arguments
        }, cancellationToken);

        return new ExpressVpnConnectionResult
        {
            IsConnected = !IsToolError(callResult),
            Message = BuildResultMessage(connectTool.Name, callResult)
        };
    }

    private async Task<JsonElement> SendJsonRpcAsync(
        Uri endpoint,
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(endpoint, new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N"),
            method,
            @params = parameters
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        CaptureSessionId(response);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(ExtractJsonPayload(body));
        var root = document.RootElement.Clone();
        if (root.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException(error.ToString());
        }

        return root.TryGetProperty("result", out var result)
            ? result
            : root;
    }

    private async Task SendNotificationAsync(Uri endpoint, string method, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(endpoint, new
        {
            jsonrpc = "2.0",
            method
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        CaptureSessionId(response);
        response.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage CreateRequest(Uri endpoint, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        if (!string.IsNullOrWhiteSpace(_sessionId))
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        }

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }

    private void CaptureSessionId(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var values))
        {
            _sessionId = values.FirstOrDefault() ?? _sessionId;
        }
    }

    private static IReadOnlyList<McpTool> ReadTools(JsonElement toolsResult)
    {
        if (!toolsResult.TryGetProperty("tools", out var toolsElement) ||
            toolsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var tools = new List<McpTool>();
        foreach (var tool in toolsElement.EnumerateArray())
        {
            if (!tool.TryGetProperty("name", out var nameElement))
            {
                continue;
            }

            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var description = tool.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.GetString() ?? string.Empty
                : string.Empty;

            var inputSchema = tool.TryGetProperty("inputSchema", out var schemaElement)
                ? schemaElement.Clone()
                : default;

            tools.Add(new McpTool(name, description, inputSchema));
        }

        return tools;
    }

    private static McpTool? FindConnectTool(IReadOnlyList<McpTool> tools)
    {
        return tools.FirstOrDefault(tool =>
                   ContainsAny(tool.Name, "connect", "vpn_connect") &&
                   !ContainsAny(tool.Name, "disconnect", "reconnect")) ??
               tools.FirstOrDefault(tool =>
                   ContainsAny(tool.Description, "connect") &&
                   ContainsAny(tool.Description, "vpn", "expressvpn") &&
                   !ContainsAny(tool.Description, "disconnect"));
    }

    private static Dictionary<string, object?> BuildToolArguments(McpTool tool, string? location)
    {
        var arguments = new Dictionary<string, object?>();
        var normalizedLocation = string.IsNullOrWhiteSpace(location) ||
                                 location.Equals("smart", StringComparison.OrdinalIgnoreCase)
            ? null
            : location.Trim();

        if (tool.InputSchema.ValueKind != JsonValueKind.Object ||
            !tool.InputSchema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            if (normalizedLocation is not null)
            {
                arguments["location"] = normalizedLocation;
            }

            return arguments;
        }

        foreach (var property in properties.EnumerateObject())
        {
            if (IsLocationProperty(property.Name) && normalizedLocation is not null)
            {
                arguments[property.Name] = normalizedLocation;
            }
        }

        return arguments;
    }

    private static bool IsLocationProperty(string propertyName)
    {
        return ContainsAny(propertyName, "location", "region", "country", "server");
    }

    private static bool IsToolError(JsonElement callResult)
    {
        return callResult.TryGetProperty("isError", out var isErrorElement) &&
               isErrorElement.ValueKind == JsonValueKind.True;
    }

    private static string BuildResultMessage(string toolName, JsonElement callResult)
    {
        var content = ReadContentText(callResult);
        return string.IsNullOrWhiteSpace(content)
            ? $"{toolName} を実行しました。"
            : content;
    }

    private static string ReadContentText(JsonElement callResult)
    {
        if (!callResult.TryGetProperty("content", out var contentElement) ||
            contentElement.ValueKind != JsonValueKind.Array)
        {
            return callResult.ToString();
        }

        var lines = new List<string>();
        foreach (var item in contentElement.EnumerateArray())
        {
            if (item.TryGetProperty("text", out var textElement))
            {
                lines.Add(textElement.GetString() ?? string.Empty);
            }
        }

        return string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string ExtractJsonPayload(string body)
    {
        if (!body.TrimStart().StartsWith("event:", StringComparison.OrdinalIgnoreCase) &&
            !body.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return body;
        }

        var dataLines = body
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            .Select(line => line["data:".Length..].Trim());

        return string.Join("", dataLines);
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record McpTool(string Name, string Description, JsonElement InputSchema);
}

file sealed class ExpressVpnMcpOptions
{
    public required string Endpoint { get; init; }
    public string? Location { get; init; }

    public static ExpressVpnMcpOptions Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new ExpressVpnMcpOptions
            {
                Endpoint = "http://127.0.0.1:20090/mcp",
                Location = null
            };
        }

        var parts = value.Split('|', 2, StringSplitOptions.TrimEntries);
        if (Uri.TryCreate(parts[0], UriKind.Absolute, out var endpoint) &&
            (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps))
        {
            return new ExpressVpnMcpOptions
            {
                Endpoint = endpoint.ToString(),
                Location = parts.Length > 1 ? parts[1] : null
            };
        }

        return new ExpressVpnMcpOptions
        {
            Endpoint = "http://127.0.0.1:20090/mcp",
            Location = value.Trim()
        };
    }
}
