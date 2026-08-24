import readline from "node:readline";

const marker = "MCP_SMOKE_SUCCESS";

function send(message) {
    process.stdout.write(`${JSON.stringify(message)}\n`);
}

function respond(id, result) {
    send({ jsonrpc: "2.0", id, result });
}

function reject(id, code, message) {
    send({ jsonrpc: "2.0", id, error: { code, message } });
}

function handle(message) {
    if (message.jsonrpc !== "2.0" || typeof message.method !== "string") {
        return;
    }

    switch (message.method) {
        case "initialize":
            respond(message.id, {
                protocolVersion: message.params?.protocolVersion ?? "2025-03-26",
                capabilities: { tools: { listChanged: false } },
                serverInfo: {
                    name: "codex-gateway-mcp-smoke",
                    version: "1.0.0"
                }
            });
            break;
        case "notifications/initialized":
        case "initialized":
            break;
        case "tools/list":
            respond(message.id, {
                tools: [
                    {
                        name: "mcp_smoke",
                        description: `Returns the exact text ${marker}. Use this tool when asked to run the gateway MCP smoke test.`,
                        inputSchema: {
                            type: "object",
                            additionalProperties: false
                        }
                    }
                ]
            });
            break;
        case "tools/call":
            if (message.params?.name !== "mcp_smoke") {
                reject(message.id, -32602, "Unknown tool.");
                break;
            }

            respond(message.id, {
                content: [{ type: "text", text: marker }]
            });
            break;
        default:
            if (message.id !== undefined) {
                reject(message.id, -32601, "Method not found.");
            }
            break;
    }
}

const input = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
input.on("line", line => {
    try {
        handle(JSON.parse(line));
    } catch {
        // STDIO MCP requires stdout to contain protocol messages only.
    }
});
