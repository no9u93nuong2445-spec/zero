using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

if (args.Length != 1 || !int.TryParse(args[0], out var port)) throw new Exception("port required");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
string json = "";
for (var i = 0; i < 40; i++)
{
    try { json = await http.GetStringAsync($"http://127.0.0.1:{port}/json/list"); if (json.Contains("webSocketDebuggerUrl")) break; } catch { }
    await Task.Delay(250);
}
if (string.IsNullOrWhiteSpace(json)) throw new Exception("no CDP target list");
using var doc = JsonDocument.Parse(json);
var wsUrl = doc.RootElement.EnumerateArray().Select(x => x.TryGetProperty("webSocketDebuggerUrl", out var p) ? p.GetString() : null).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
if (wsUrl is null) throw new Exception("no websocket debugger URL");
using var ws = new ClientWebSocket();
await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
var payload = Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"Network.enable\",\"params\":{}}");
await ws.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
var buffer = new byte[64 * 1024];
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
while (true)
{
    var r = await ws.ReceiveAsync(buffer, cts.Token);
    var text = Encoding.UTF8.GetString(buffer, 0, r.Count);
    if (text.Contains("\"id\":1")) break;
}
Console.WriteLine("PASS: localhost CDP websocket attach and Network.enable");
